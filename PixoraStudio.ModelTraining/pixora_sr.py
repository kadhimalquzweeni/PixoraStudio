"""Pixora's original 4x super-resolution network, initialized without pretrained weights."""
from __future__ import annotations

import torch
from torch import nn
from torch.nn import functional as F


class ResidualBlock(nn.Module):
    def __init__(self, channels: int) -> None:
        super().__init__()
        self.layers = nn.Sequential(
            nn.Conv2d(channels, channels, 3, padding=1),
            nn.PReLU(channels),
            nn.Conv2d(channels, channels, 3, padding=1),
        )

    def forward(self, value: torch.Tensor) -> torch.Tensor:
        return value + self.layers(value) * 0.2


class PixoraSR(nn.Module):
    """Compact residual 4x upscaler that learns detail over a bicubic baseline."""

    scale = 4

    def __init__(self, channels: int = 48, residual_blocks: int = 8) -> None:
        super().__init__()
        self.first = nn.Conv2d(3, channels, 3, padding=1)
        self.blocks = nn.Sequential(*(ResidualBlock(channels) for _ in range(residual_blocks)))
        self.trunk = nn.Conv2d(channels, channels, 3, padding=1)
        self.upscale_2x_a = nn.Sequential(
            nn.Conv2d(channels, channels * 4, 3, padding=1),
            nn.PixelShuffle(2),
            nn.PReLU(channels),
        )
        self.upscale_2x_b = nn.Sequential(
            nn.Conv2d(channels, channels * 4, 3, padding=1),
            nn.PixelShuffle(2),
            nn.PReLU(channels),
        )
        self.to_rgb = nn.Conv2d(channels, 3, 3, padding=1)

        # Start with the bicubic baseline, then let training learn a residual.
        nn.init.zeros_(self.to_rgb.weight)
        nn.init.zeros_(self.to_rgb.bias)

    def forward(self, low_resolution: torch.Tensor) -> torch.Tensor:
        features = self.first(low_resolution)
        features = features + self.trunk(self.blocks(features))
        features = self.upscale_2x_a(features)
        features = self.upscale_2x_b(features)
        detail = self.to_rgb(features)
        baseline = F.interpolate(
            low_resolution,
            scale_factor=self.scale,
            mode="bicubic",
            align_corners=False,
        )
        return baseline + detail
