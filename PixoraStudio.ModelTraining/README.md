# Pixora Studio image upscaler training

This folder contains Pixora's own small 4x super-resolution network and its training scripts. The network starts from random weights and does not use a pretrained model. Training images are collected separately and are not bundled in the repository.

## 1. Install Python packages

Use Python 3.10 or newer. Create a virtual environment, then install PyTorch using the command from the official selector at https://pytorch.org/get-started/locally/ so it matches your computer and CUDA driver. Then install the remaining packages:

```powershell
py -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r requirements.txt
```

For GPU training, choose the CUDA-enabled PyTorch install command from the PyTorch selector. CPU training is supported but can take much longer.

## 2. Add training images

Create `data/hr` and place high-resolution images you own or have permission to use there. Images must be at least 256 x 256 pixels for the default patch size. The script makes random crops, flips, and rotations and creates synthetic 4x low-resolution inputs from those crops. The original files are read locally and are not uploaded.

```powershell
New-Item -ItemType Directory -Force data\hr
```

A small or narrow dataset may train poorly or overfit. More varied, detailed images and longer training usually produce a better model. Training from scratch needs substantial GPU time.

## 3. Train the photo model

From this folder:

```powershell
python train.py --data-dir data/hr --epochs 50 --steps-per-epoch 500 --batch-size 8 --patch-size 64
```

Checkpoints are saved under `checkpoints\`. Training starts from random initialization by default. To continue one of Pixora's own saved runs, pass `--resume checkpoints\pixora_sr_x4_last.pt`.

After training, the script exports `pixora_sr_x4.onnx` to:

```
%LOCALAPPDATA%\PixoraStudio\Models\pixora_sr_x4.onnx
```

You can choose another destination with `--output`. Copy the resulting model to that local path if you use a custom output path. Restart Pixora Studio after placing a model there. The Save dialog's Pixora AI 4x option becomes available when the model is present.

## 4. Train the sprite / emoji model

The sprite trainer uses the Noto Emoji 128px artwork collection. Its collection script pins an upstream commit, keeps a per-image attribution CSV and license notes, and excludes regional and tag-sequence flags. The image loader retains each PNG's alpha channel and weights the RGB and edge losses by visible alpha, so fully transparent pixels do not train black halos.

From this folder, collect the assets and train the separate sprite model:

```powershell
python collect_noto_emoji.py
python train.py --model-kind sprite --data-dir data/hr/noto_emoji_128 --epochs 50 --steps-per-epoch 500 --batch-size 8 --patch-size 32
```

`--patch-size 32` trains 32px inputs against 128px targets. The sprite model is exported to `%LOCALAPPDATA%\PixoraStudio\Models\pixora_sr_sprite_x4.onnx`; it does not replace the photo model. Both models can be selected in the Save dialog when their ONNX files are present.

The editor uses ONNX Runtime DirectML on Windows when available and falls back to CPU inference. The app processes image tiles to limit working memory. Both models support 4x RGB inference; the existing alpha channel is scaled separately and retained in the saved result.
