"""Train PixoraSR from scratch using user-supplied high-resolution images."""
from __future__ import annotations

import argparse
import os
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageOps
import torch
from torch import nn
from torch.nn import functional as F
from torch.utils.data import DataLoader, Dataset

from pixora_sr import PixoraSR

IMAGE_SUFFIXES = {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff"}


class HighResolutionPatches(Dataset):
    def __init__(self, folder: Path, patch_size: int) -> None:
        self.patch_size = patch_size
        self.hr_size = patch_size * PixoraSR.scale
        self.paths: list[Path] = []
        for path in sorted(folder.rglob("*")):
            if path.suffix.lower() not in IMAGE_SUFFIXES:
                continue
            try:
                with Image.open(path) as image:
                    if image.width >= self.hr_size and image.height >= self.hr_size:
                        self.paths.append(path)
            except (OSError, ValueError):
                continue
        if not self.paths:
            raise SystemExit(
                "No readable images large enough for a crop were found. "
                f"Add images at least {self.hr_size} x {self.hr_size} pixels to {folder}."
            )

    def __len__(self) -> int:
        return max(1000, len(self.paths) * 100)

    def __getitem__(self, index: int) -> tuple[torch.Tensor, torch.Tensor]:
        path = self.paths[index % len(self.paths)]
        with Image.open(path) as opened:
            image = ImageOps.exif_transpose(opened).convert("RGBA")
            width, height = image.size
            left = random.randint(0, width - self.hr_size)
            top = random.randint(0, height - self.hr_size)
            crop = image.crop((left, top, left + self.hr_size, top + self.hr_size))

        array = np.asarray(crop, dtype=np.float32) / 255.0
        if random.random() < 0.5:
            array = array[:, ::-1]
        if random.random() < 0.5:
            array = array[::-1]
        turns = random.randrange(4)
        if turns:
            array = np.rot90(array, turns)
        array = np.ascontiguousarray(array.transpose(2, 0, 1))
        rgb = torch.from_numpy(array[:3])
        alpha = torch.from_numpy(array[3:4])
        return rgb, alpha


def alpha_aware_loss(
    prediction: torch.Tensor,
    target: torch.Tensor,
    alpha: torch.Tensor,
) -> torch.Tensor:
    """Train RGB detail where it is visible, while retaining smooth sprite edges."""
    rgb_weight = alpha.expand_as(target)
    rgb_loss = ((prediction - target).abs() * rgb_weight).sum() / rgb_weight.sum().clamp_min(1.0)

    dx_prediction = prediction[:, :, :, 1:] - prediction[:, :, :, :-1]
    dx_target = target[:, :, :, 1:] - target[:, :, :, :-1]
    dx_weight = torch.maximum(alpha[:, :, :, 1:], alpha[:, :, :, :-1]).expand_as(dx_target)
    dx_loss = ((dx_prediction - dx_target).abs() * dx_weight).sum() / dx_weight.sum().clamp_min(1.0)

    dy_prediction = prediction[:, :, 1:, :] - prediction[:, :, :-1, :]
    dy_target = target[:, :, 1:, :] - target[:, :, :-1, :]
    dy_weight = torch.maximum(alpha[:, :, 1:, :], alpha[:, :, :-1, :]).expand_as(dy_target)
    dy_loss = ((dy_prediction - dy_target).abs() * dy_weight).sum() / dy_weight.sum().clamp_min(1.0)
    return rgb_loss + 0.05 * (dx_loss + dy_loss)


def choose_device(requested: str) -> torch.device:
    if requested != "auto":
        return torch.device(requested)
    if torch.cuda.is_available():
        return torch.device("cuda")
    if hasattr(torch.backends, "mps") and torch.backends.mps.is_available():
        return torch.device("mps")
    return torch.device("cpu")


def default_model_path(model_kind: str) -> Path:
    local_app_data = os.environ.get("LOCALAPPDATA")
    if not local_app_data:
        local_app_data = str(Path.home() / "AppData" / "Local")
    filename = "pixora_sr_sprite_x4.onnx" if model_kind == "sprite" else "pixora_sr_x4.onnx"
    return Path(local_app_data) / "PixoraStudio" / "Models" / filename


def export_model(model: PixoraSR, target: Path, device: torch.device) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    model = model.to("cpu").eval()
    sample = torch.zeros(1, 3, 32, 40, dtype=torch.float32)
    torch.onnx.export(
        model,
        (sample,),
        str(target),
        input_names=["input"],
        output_names=["output"],
        opset_version=17,
        dynamo=False,
        dynamic_axes={
            "input": {0: "batch", 2: "height", 3: "width"},
            "output": {0: "batch", 2: "output_height", 3: "output_width"},
        },
    )
    import onnx
    onnx.checker.check_model(str(target))
    print(f"ONNX model exported to: {target}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Train Pixora's original 4x image upscaler.")
    parser.add_argument("--data-dir", type=Path, default=Path("data/hr"),
                        help="Folder containing high-resolution images you have permission to use.")
    parser.add_argument("--epochs", type=int, default=50)
    parser.add_argument("--steps-per-epoch", type=int, default=500)
    parser.add_argument("--patch-size", type=int, default=64,
                        help="Low-resolution crop size; the matching high-resolution crop is 4x larger. Use 32 for 128px sprite targets.")
    parser.add_argument("--batch-size", type=int, default=8)
    parser.add_argument("--workers", type=int, default=0,
                        help="Data-loader workers. Zero is the Windows-safe default.")
    parser.add_argument("--learning-rate", type=float, default=0.0002)
    parser.add_argument("--device", default="auto",
                        help="auto, cpu, cuda, cuda:0, or mps.")
    parser.add_argument("--model-kind", choices=("photo", "sprite"), default="photo",
                        help="Select the local output model; sprite writes a separate model file.")
    parser.add_argument("--checkpoint-dir", type=Path, default=Path("checkpoints"))
    parser.add_argument("--resume", type=Path, default=None,
                        help="Optional Pixora checkpoint to continue training.")
    parser.add_argument("--output", type=Path, default=None,
                        help="ONNX output path; defaults to the Pixora Studio local model folder.")
    args = parser.parse_args()

    if args.patch_size < 8 or args.epochs < 1 or args.steps_per_epoch < 1 or args.batch_size < 1:
        parser.error("patch size must be at least 8; epochs, steps, and batch size must be positive")

    device = choose_device(args.device)
    print(f"Training PixoraSR from random initialization on {device}.")
    dataset = HighResolutionPatches(args.data_dir, args.patch_size)
    print(f"Using {len(dataset.paths)} user-supplied source images.")
    loader = DataLoader(
        dataset,
        batch_size=args.batch_size,
        num_workers=args.workers,
        pin_memory=device.type == "cuda",
        drop_last=True,
    )
    iterator = iter(loader)
    model = PixoraSR().to(device)
    optimizer = torch.optim.AdamW(model.parameters(), lr=args.learning_rate, betas=(0.9, 0.99))
    start_epoch = 1
    if args.resume is not None:
        checkpoint = torch.load(args.resume, map_location=device, weights_only=False)
        model.load_state_dict(checkpoint["model"])
        optimizer.load_state_dict(checkpoint["optimizer"])
        start_epoch = int(checkpoint["epoch"]) + 1
        print(f"Continuing Pixora training from epoch {start_epoch}.")

    scaler = torch.amp.GradScaler("cuda", enabled=device.type == "cuda")
    model.train()
    args.checkpoint_dir.mkdir(parents=True, exist_ok=True)

    for epoch in range(start_epoch, args.epochs + 1):
        running_loss = 0.0
        for step in range(1, args.steps_per_epoch + 1):
            try:
                high_resolution, alpha = next(iterator)
            except StopIteration:
                iterator = iter(loader)
                high_resolution, alpha = next(iterator)
            high_resolution = high_resolution.to(device, non_blocking=device.type == "cuda")
            alpha = alpha.to(device, non_blocking=device.type == "cuda")
            low_resolution = F.interpolate(
                high_resolution,
                size=(args.patch_size, args.patch_size),
                mode="bicubic",
                align_corners=False,
                antialias=True,
            )
            optimizer.zero_grad(set_to_none=True)
            with torch.autocast(device_type=device.type, dtype=torch.float16,
                                enabled=device.type == "cuda"):
                prediction = model(low_resolution)
                loss = alpha_aware_loss(prediction, high_resolution, alpha)
            scaler.scale(loss).backward()
            scaler.unscale_(optimizer)
            nn.utils.clip_grad_norm_(model.parameters(), max_norm=1.0)
            scaler.step(optimizer)
            scaler.update()
            running_loss += float(loss.detach().item())
            if step % 50 == 0 or step == args.steps_per_epoch:
                average = running_loss / step
                print(f"epoch {epoch}/{args.epochs} step {step}/{args.steps_per_epoch} "
                      f"loss {average:.5f}")

        checkpoint_path = args.checkpoint_dir / "pixora_sr_x4_last.pt"
        torch.save(
            {"epoch": epoch, "model": model.state_dict(), "optimizer": optimizer.state_dict()},
            checkpoint_path,
        )

    output_path = args.output or default_model_path(args.model_kind)
    export_model(model, output_path, device)


if __name__ == "__main__":
    main()
