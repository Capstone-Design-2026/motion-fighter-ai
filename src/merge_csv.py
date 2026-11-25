"""Merge raw motion CSV files into one training dataset."""

from pathlib import Path

import pandas as pd

from config import MERGED_CSV_PATH, MERGED_DATA_DIR, RAW_DATA_DIR


CANONICAL_COLUMNS = [
    name
    for landmark_idx in range(33)
    for name in (
        f"x{landmark_idx}",
        f"y{landmark_idx}",
        f"z{landmark_idx}",
        f"v{landmark_idx}",
    )
] + ["label"]


def find_csv_files(raw_data_dir: Path) -> list[Path]:
    """Return all CSV files in the raw data directory."""
    if not raw_data_dir.exists():
        raise FileNotFoundError(
            f"Raw data directory not found: {raw_data_dir}\n"
            "Collect or place CSV files in data/raw first."
        )

    return sorted(raw_data_dir.glob("*.csv"))


def validate_csv_schema(df: pd.DataFrame, csv_path: Path):
    """Fail fast when a raw CSV does not match the canonical feature schema."""
    columns = list(df.columns)
    if columns == CANONICAL_COLUMNS:
        return

    mismatch_index = next(
        (
            idx
            for idx, (actual, expected) in enumerate(zip(columns, CANONICAL_COLUMNS))
            if actual != expected
        ),
        None,
    )

    if mismatch_index is None and len(columns) != len(CANONICAL_COLUMNS):
        mismatch_detail = (
            f"column count {len(columns)} != {len(CANONICAL_COLUMNS)}"
        )
    elif mismatch_index is None:
        mismatch_detail = "unknown schema mismatch"
    else:
        mismatch_detail = (
            f"column {mismatch_index}: "
            f"expected '{CANONICAL_COLUMNS[mismatch_index]}', "
            f"found '{columns[mismatch_index]}'"
        )

    raise ValueError(
        "CSV schema mismatch. Refusing to merge incompatible training data.\n"
        f"File: {csv_path}\n"
        f"Problem: {mismatch_detail}"
    )


def read_valid_csv(csv_path: Path):
    """Read one CSV and return a DataFrame when its schema is valid."""
    try:
        df = pd.read_csv(csv_path)
    except pd.errors.EmptyDataError:
        print(f"[WARN] Skipping empty CSV: {csv_path}")
        return None

    if df.empty:
        print(f"[WARN] Skipping CSV with no rows: {csv_path}")
        return None

    validate_csv_schema(df, csv_path)
    return df


def load_valid_dataframes(csv_files: list[Path]) -> list[pd.DataFrame]:
    """Load all valid raw CSV files."""
    dataframes = []

    for csv_path in csv_files:
        df = read_valid_csv(csv_path)
        if df is None:
            continue

        dataframes.append(df)
        print(f"[OK] Loaded {csv_path}: {len(df)} rows")

    return dataframes


def merge_dataframes(dataframes: list[pd.DataFrame]) -> pd.DataFrame:
    """Concatenate all valid raw dataframes."""
    if not dataframes:
        raise ValueError(
            "No valid CSV files found.\n"
            "Place at least one non-empty motion_data CSV in data/raw."
        )

    return pd.concat(dataframes, ignore_index=True)


def save_merged_csv(df: pd.DataFrame):
    """Save the merged dataset to the configured output path."""
    MERGED_DATA_DIR.mkdir(parents=True, exist_ok=True)
    df.to_csv(MERGED_CSV_PATH, index=False)


def print_summary(df: pd.DataFrame):
    """Print merged dataset size and label distribution."""
    print("\n=== Merge Summary ===")
    print(f"Output: {MERGED_CSV_PATH}")
    print(f"Total rows: {len(df)}")
    print("\nLabel counts:")
    print(df["label"].value_counts().to_string())


def main():
    """Run the raw CSV merge pipeline."""
    try:
        csv_files = find_csv_files(RAW_DATA_DIR)

        if not csv_files:
            raise ValueError(
                f"No CSV files found in {RAW_DATA_DIR}.\n"
                "Expected files such as motion_data.csv or motion_data_name.csv."
            )

        print("=== Motion Fighter CSV Merger ===")
        print(f"Raw directory: {RAW_DATA_DIR}")
        print(f"Found CSV files: {len(csv_files)}")

        dataframes = load_valid_dataframes(csv_files)
        merged_df = merge_dataframes(dataframes)
        save_merged_csv(merged_df)
        print_summary(merged_df)

    except (FileNotFoundError, ValueError) as exc:
        raise SystemExit(f"\n[ERROR] {exc}") from exc


if __name__ == "__main__":
    main()
