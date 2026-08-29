#!/usr/bin/env python3
"""Convert a discrete hypsometric colour map into a deterministic height map.

The source artwork stores elevation as flat colour bands.  This tool extracts
the ordered palette, preserves every authored band boundary, and interpolates
only inside each band.  It also flood-fills the edge-connected background into
an explicit playable-area mask, which is required when a terrain colour is
also used by the artwork background.

Only Pillow and NumPy are required.
"""

from __future__ import annotations

import argparse
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--colour-map", type=Path, required=True)
    parser.add_argument("--palette", type=Path, required=True)
    parser.add_argument("--output-height", type=Path, required=True)
    parser.add_argument("--output-preview", type=Path, required=True)
    parser.add_argument("--output-mask", type=Path, required=True)
    parser.add_argument("--resolution", type=int, default=2048)
    parser.add_argument("--height-step-metres", type=float, default=5.0)
    return parser.parse_args()


def extract_palette_low_to_high(palette_path: Path) -> np.ndarray:
    """Read the 3x10 supplied swatch sheet in reverse row-major order.

    The sheet has a 16 px frame and 72 px cells.  Empty dark-grey cells are
    discarded using chroma, leaving the 17 authored elevation colours.
    Sampling medians from the middle half of each cell avoids JPEG seams.
    """

    image = np.asarray(Image.open(palette_path).convert("RGB"), dtype=np.uint8)
    left = 16
    top = 16
    cell_width = 72
    cell_height = 72
    rows = 3
    columns = 10

    swatches: list[np.ndarray] = []
    for row in range(rows):
        for column in range(columns):
            x0 = left + column * cell_width + cell_width // 4
            x1 = left + (column + 1) * cell_width - cell_width // 4
            y0 = top + row * cell_height + cell_height // 4
            y1 = top + (row + 1) * cell_height - cell_height // 4
            sample = image[y0:y1, x0:x1].reshape(-1, 3)
            colour = np.median(sample, axis=0).astype(np.uint8)
            if int(colour.max()) - int(colour.min()) >= 20:
                swatches.append(colour)

    if len(swatches) < 2:
        raise RuntimeError(
            f"Could not extract the coloured cells from {palette_path}."
        )

    # The user's elevation order is lower-right -> upper-left.  The coloured
    # cells occupy the beginning of normal row-major order, so reverse it.
    return np.stack(swatches[::-1], axis=0)


def square_resize_nearest(source: Image.Image, resolution: int) -> Image.Image:
    """Pad to square before resizing so the source geography is not stretched."""

    width, height = source.size
    side = max(width, height)
    background = source.convert("RGB").getpixel((0, 0))
    square = Image.new("RGB", (side, side), background)
    offset_x = (side - width) // 2
    offset_y = (side - height) // 2
    square.paste(source.convert("RGB"), (offset_x, offset_y))
    return square.resize((resolution, resolution), Image.Resampling.NEAREST)


def scanline_component(
    candidate: np.ndarray,
    seed: tuple[int, int],
) -> np.ndarray:
    """Return one four-connected component of a boolean image."""

    height, width = candidate.shape
    connected = np.zeros((height, width), dtype=np.bool_)
    stack: deque[tuple[int, int]] = deque([seed])

    while stack:
        x, y = stack.pop()
        if connected[y, x] or not candidate[y, x]:
            continue

        left = x
        while left > 0 and candidate[y, left - 1] and not connected[y, left - 1]:
            left -= 1

        right = x
        while (
            right + 1 < width
            and candidate[y, right + 1]
            and not connected[y, right + 1]
        ):
            right += 1

        connected[y, left : right + 1] = True

        for adjacent_y in (y - 1, y + 1):
            if adjacent_y < 0 or adjacent_y >= height:
                continue
            scan_x = left
            while scan_x <= right:
                if candidate[adjacent_y, scan_x] and not connected[adjacent_y, scan_x]:
                    stack.append((scan_x, adjacent_y))
                    scan_x += 1
                    while (
                        scan_x <= right
                        and candidate[adjacent_y, scan_x]
                        and not connected[adjacent_y, scan_x]
                    ):
                        scan_x += 1
                else:
                    scan_x += 1

    return connected


def edge_connected_background(rgb: np.ndarray) -> np.ndarray:
    """Return the exact-colour component connected to image (0, 0).

    The supplied brown background is also a legitimate 45 m terrain swatch, so
    connectivity is the only lossless way to distinguish the two.
    """

    background_colour = rgb[0, 0]
    candidate = np.all(rgb == background_colour, axis=2)
    return scanline_component(candidate, (0, 0))


def main_terrain_component(non_background: np.ndarray) -> np.ndarray:
    """Keep the single authored landmass and reject isolated source specks."""

    height, width = non_background.shape
    centre_x = width // 2
    centre_y = height // 2
    if non_background[centre_y, centre_x]:
        return scanline_component(non_background, (centre_x, centre_y))

    y_coordinates, x_coordinates = np.nonzero(non_background)
    if x_coordinates.size == 0:
        raise RuntimeError("No terrain pixels remain after removing the background.")
    squared_distance = (
        (x_coordinates - centre_x) * (x_coordinates - centre_x)
        + (y_coordinates - centre_y) * (y_coordinates - centre_y)
    )
    nearest = int(np.argmin(squared_distance))
    return scanline_component(
        non_background,
        (int(x_coordinates[nearest]), int(y_coordinates[nearest])),
    )


def classify_palette(
    rgb: np.ndarray,
    palette: np.ndarray,
) -> tuple[np.ndarray, np.ndarray, float, int]:
    """Classify the lossless map colours against the JPEG palette medians."""

    flat = rgb.reshape(-1, 3)
    unique_colours, inverse = np.unique(flat, axis=0, return_inverse=True)
    difference = (
        unique_colours[:, None, :].astype(np.int32)
        - palette[None, :, :].astype(np.int32)
    )
    squared_distance = np.sum(difference * difference, axis=2)
    nearest = np.argmin(squared_distance, axis=1).astype(np.int16)
    labels = nearest[inverse].reshape(rgb.shape[:2])
    nearest_distance = np.sqrt(np.min(squared_distance, axis=1))
    valid_unique = nearest_distance <= 12.0
    valid = valid_unique[inverse].reshape(rgb.shape[:2])
    worst_valid_distance = float(nearest_distance[valid_unique].max())
    invalid_pixel_count = int(np.count_nonzero(~valid))
    return labels, valid, worst_valid_distance, invalid_pixel_count


def repair_invalid_labels(
    labels: np.ndarray,
    valid: np.ndarray,
    playable_mask: np.ndarray,
) -> np.ndarray:
    """Replace rare non-palette source specks from their immediate terrain."""

    repaired = labels.copy()
    unresolved = playable_mask & ~valid
    while np.any(unresolved):
        changed = False
        for y, x in np.argwhere(unresolved):
            y0 = max(0, int(y) - 1)
            y1 = min(labels.shape[0], int(y) + 2)
            x0 = max(0, int(x) - 1)
            x1 = min(labels.shape[1], int(x) + 2)
            neighbours = repaired[y0:y1, x0:x1]
            usable = valid[y0:y1, x0:x1] & playable_mask[y0:y1, x0:x1]
            if not np.any(usable):
                continue
            repaired[y, x] = int(np.median(neighbours[usable]))
            valid[y, x] = True
            unresolved[y, x] = False
            changed = True
        if not changed:
            raise RuntimeError("Could not repair an isolated non-palette terrain pixel.")
    return repaired


def mask_boundary(mask: np.ndarray) -> np.ndarray:
    boundary = np.zeros_like(mask)
    boundary[0, :] |= mask[0, :]
    boundary[-1, :] |= mask[-1, :]
    boundary[:, 0] |= mask[:, 0]
    boundary[:, -1] |= mask[:, -1]

    vertical = mask[:-1, :] != mask[1:, :]
    boundary[:-1, :] |= vertical & mask[:-1, :]
    boundary[1:, :] |= vertical & mask[1:, :]
    horizontal = mask[:, :-1] != mask[:, 1:]
    boundary[:, :-1] |= horizontal & mask[:, :-1]
    boundary[:, 1:] |= horizontal & mask[:, 1:]
    return boundary


def contour_boundary(labels: np.ndarray, mask: np.ndarray, threshold: int) -> np.ndarray:
    high_side = labels >= threshold
    boundary = np.zeros_like(mask)

    vertical = (
        mask[:-1, :]
        & mask[1:, :]
        & (high_side[:-1, :] != high_side[1:, :])
    )
    boundary[:-1, :] |= vertical
    boundary[1:, :] |= vertical

    horizontal = (
        mask[:, :-1]
        & mask[:, 1:]
        & (high_side[:, :-1] != high_side[:, 1:])
    )
    boundary[:, :-1] |= horizontal
    boundary[:, 1:] |= horizontal
    return boundary


def horizontal_min_plus(row: np.ndarray, x_axis: np.ndarray) -> np.ndarray:
    """One-dimensional min-plus transform with unit horizontal cost."""

    left = np.minimum.accumulate(row - x_axis) + x_axis
    right = np.minimum.accumulate((row + x_axis)[::-1])[::-1] - x_axis
    return np.minimum(left, right)


def chamfer_distance(seeds: np.ndarray) -> np.ndarray:
    """Approximate Euclidean distance using an 8-neighbour chamfer metric."""

    height, width = seeds.shape
    infinity = np.float32(1.0e7)
    diagonal = np.float32(np.sqrt(2.0))
    x_axis = np.arange(width, dtype=np.float32)
    distance = np.where(seeds, np.float32(0.0), infinity).astype(np.float32)

    for y in range(height):
        row = distance[y].copy()
        if y > 0:
            previous = distance[y - 1]
            row = np.minimum(row, previous + np.float32(1.0))
            row[1:] = np.minimum(row[1:], previous[:-1] + diagonal)
            row[:-1] = np.minimum(row[:-1], previous[1:] + diagonal)
        distance[y] = horizontal_min_plus(row, x_axis)

    for y in range(height - 1, -1, -1):
        row = distance[y].copy()
        if y + 1 < height:
            following = distance[y + 1]
            row = np.minimum(row, following + np.float32(1.0))
            row[1:] = np.minimum(row[1:], following[:-1] + diagonal)
            row[:-1] = np.minimum(row[:-1], following[1:] + diagonal)
        distance[y] = horizontal_min_plus(row, x_axis)

    return distance


def interpolate_height_bands(
    labels: np.ndarray,
    playable_mask: np.ndarray,
    height_step_metres: float,
) -> tuple[np.ndarray, int]:
    """Interpolate within bands without moving their authored boundaries.

    For band k, the fraction between its lower and upper contours is
    d(lower) / (d(lower) + d(upper)).  Therefore both pixels touching a
    shared contour resolve to the same exact multiple of the 5 m step.
    """

    active_labels = labels[playable_mask]
    if active_labels.size == 0:
        raise RuntimeError("The generated playable-area mask is empty.")
    minimum_label = int(active_labels.min())
    maximum_label = int(active_labels.max())
    if minimum_label != 0:
        raise RuntimeError(
            f"The lowest colour used by the terrain is palette index {minimum_label}, not 0."
        )

    shape = labels.shape
    lower_distance = np.full(shape, np.nan, dtype=np.float32)
    upper_distance = np.full(shape, np.nan, dtype=np.float32)

    distance_to_edge = chamfer_distance(mask_boundary(playable_mask))
    lowest_band = playable_mask & (labels == 0)
    lower_distance[lowest_band] = distance_to_edge[lowest_band]

    for threshold in range(1, maximum_label + 1):
        seeds = contour_boundary(labels, playable_mask, threshold)
        if not np.any(seeds):
            raise RuntimeError(f"No contour found for palette threshold {threshold}.")
        print(f"  interpolating contour {threshold}/{maximum_label}", flush=True)
        distance = chamfer_distance(seeds)
        below = playable_mask & (labels == threshold - 1)
        above = playable_mask & (labels == threshold)
        upper_distance[below] = distance[below]
        lower_distance[above] = distance[above]

    heights = np.zeros(shape, dtype=np.float32)
    for level in range(maximum_label):
        pixels = playable_mask & (labels == level)
        lower = lower_distance[pixels]
        upper = upper_distance[pixels]
        missing_lower = ~np.isfinite(lower)
        missing_upper = ~np.isfinite(upper)
        lower[missing_lower] = np.float32(1.0)
        upper[missing_upper] = np.float32(1.0)
        denominator = lower + upper
        fraction = np.divide(
            lower,
            denominator,
            out=np.full_like(lower, np.float32(0.5)),
            where=denominator > np.float32(1.0e-5),
        )
        heights[pixels] = (np.float32(level) + fraction) * np.float32(
            height_step_metres
        )

    # No higher contour exists for the summit colour.  Keeping it at its known
    # elevation is conservative and avoids inventing an unsupported peak.
    summit = playable_mask & (labels == maximum_label)
    heights[summit] = np.float32(maximum_label * height_step_metres)
    return heights, maximum_label


def save_outputs(
    heights_metres: np.ndarray,
    playable_mask: np.ndarray,
    maximum_height_metres: float,
    output_height: Path,
    output_preview: Path,
    output_mask: Path,
) -> None:
    for output in (output_height, output_preview, output_mask):
        output.parent.mkdir(parents=True, exist_ok=True)

    normalized = np.clip(heights_metres / maximum_height_metres, 0.0, 1.0)
    height_u16 = np.rint(normalized * 65535.0).astype(np.uint16)
    height_u8 = np.rint(normalized * 255.0).astype(np.uint8)
    mask_u8 = playable_mask.astype(np.uint8) * np.uint8(255)

    Image.fromarray(height_u16).save(output_height)
    Image.fromarray(height_u8).save(output_preview)
    Image.fromarray(mask_u8).save(output_mask)


def main() -> None:
    args = parse_args()
    if args.resolution < 128:
        raise ValueError("Resolution must be at least 128.")
    if args.height_step_metres <= 0:
        raise ValueError("Height step must be positive.")

    palette = extract_palette_low_to_high(args.palette)
    print(f"Extracted {len(palette)} palette colours (low -> high):")
    for index, colour in enumerate(palette):
        height = index * args.height_step_metres
        print(f"  {index:2d}: {tuple(int(v) for v in colour)} = {height:g} m")

    source = Image.open(args.colour_map)
    resized = square_resize_nearest(source, args.resolution)
    rgb = np.asarray(resized, dtype=np.uint8)
    connected_background = edge_connected_background(rgb)
    playable_mask = main_terrain_component(~connected_background)
    (
        labels,
        valid_palette_pixels,
        worst_palette_distance,
        invalid_palette_pixel_count,
    ) = classify_palette(
        rgb,
        palette,
    )
    labels = repair_invalid_labels(
        labels,
        valid_palette_pixels,
        playable_mask,
    )
    print(f"Worst accepted source-to-palette RGB distance: {worst_palette_distance:.2f}")
    if invalid_palette_pixel_count > 0:
        print(
            f"Rejected/repaired {invalid_palette_pixel_count} non-palette source pixels."
        )

    heights, maximum_label = interpolate_height_bands(
        labels,
        playable_mask,
        args.height_step_metres,
    )
    maximum_height_metres = maximum_label * args.height_step_metres
    if maximum_height_metres <= 0:
        raise RuntimeError("The map does not contain a positive elevation range.")

    save_outputs(
        heights,
        playable_mask,
        maximum_height_metres,
        args.output_height,
        args.output_preview,
        args.output_mask,
    )

    coverage = 100.0 * float(np.count_nonzero(playable_mask)) / playable_mask.size
    print(
        f"Generated {args.resolution}x{args.resolution} map; "
        f"known range 0-{maximum_height_metres:g} m; "
        f"playable coverage {coverage:.2f}%"
    )


if __name__ == "__main__":
    main()
