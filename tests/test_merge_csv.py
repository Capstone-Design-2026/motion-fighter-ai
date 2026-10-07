"""Unit tests for raw CSV schema validation."""

from pathlib import Path
import sys
import unittest

import pandas as pd


SRC_DIR = Path(__file__).resolve().parent.parent / "src"
sys.path.insert(0, str(SRC_DIR))

from merge_csv import CANONICAL_COLUMNS, validate_csv_schema  # noqa: E402


class MergeCsvSchemaTest(unittest.TestCase):
    def test_accepts_canonical_schema(self):
        df = pd.DataFrame(columns=CANONICAL_COLUMNS)

        validate_csv_schema(df, Path("valid.csv"))

    def test_rejects_same_column_count_with_wrong_order(self):
        columns = CANONICAL_COLUMNS.copy()
        columns[0], columns[1] = columns[1], columns[0]
        df = pd.DataFrame(columns=columns)

        with self.assertRaisesRegex(ValueError, "wrong_order.csv"):
            validate_csv_schema(df, Path("wrong_order.csv"))

    def test_rejects_missing_column(self):
        df = pd.DataFrame(columns=CANONICAL_COLUMNS[:-1])

        with self.assertRaisesRegex(ValueError, "missing_label.csv"):
            validate_csv_schema(df, Path("missing_label.csv"))


if __name__ == "__main__":
    unittest.main()
