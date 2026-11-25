"""Train an MLP motion classifier from collected pose landmark CSV data."""

from pathlib import Path

import numpy as np
import pandas as pd
from sklearn.metrics import classification_report, confusion_matrix
from sklearn.model_selection import train_test_split
from sklearn.preprocessing import LabelEncoder
from tensorflow.keras.layers import Dense, Dropout, Input
from tensorflow.keras.models import Sequential

from config import (
    LABEL_CLASSES_PATH,
    MERGED_CSV_PATH,
    MODEL_DIR,
    RAW_CSV_PATH,
    TRAINED_MODEL_PATH,
)


MIN_SAMPLES_PER_LABEL = 2


def get_dataset_path() -> Path:
    """Choose the merged dataset first, then fall back to the raw dataset."""
    if MERGED_CSV_PATH.exists():
        print(f"Using merged dataset: {MERGED_CSV_PATH}")
        return MERGED_CSV_PATH

    if RAW_CSV_PATH.exists():
        print(f"Using raw dataset: {RAW_CSV_PATH}")
        return RAW_CSV_PATH

    raise FileNotFoundError(
        "No training dataset found.\n"
        f"Checked merged dataset: {MERGED_CSV_PATH}\n"
        f"Checked raw dataset: {RAW_CSV_PATH}\n"
        "Collect data or merge raw CSV files first:\n"
        "  python src/collect_data.py\n"
        "  python src/merge_csv.py"
    )


def load_dataset(csv_path: Path) -> pd.DataFrame:
    """Load the raw motion CSV and validate the minimum training requirements."""
    df = pd.read_csv(csv_path)

    if df.empty:
        raise ValueError(
            f"Dataset is empty: {csv_path}\n"
            "Collect labeled pose samples before training."
        )

    if "label" not in df.columns:
        raise ValueError(
            "Dataset must contain a 'label' column as the last column.\n"
            f"Found columns: {list(df.columns)}"
        )

    label_counts = df["label"].value_counts()
    if len(label_counts) < 2:
        raise ValueError(
            "Training requires at least 2 different labels.\n"
            f"Current label counts:\n{label_counts.to_string()}"
        )

    rare_labels = label_counts[label_counts < MIN_SAMPLES_PER_LABEL]
    if not rare_labels.empty:
        raise ValueError(
            "Each label needs at least 2 samples for stratified train/test split.\n"
            f"Labels with too few samples:\n{rare_labels.to_string()}"
        )

    return df


def split_features_and_labels(df: pd.DataFrame):
    """Split feature columns from the final label column."""
    X = df.drop(columns=["label"]).astype("float32").values
    y_labels = df["label"].astype(str).values

    label_encoder = LabelEncoder()
    y = label_encoder.fit_transform(y_labels)

    return X, y, label_encoder


def build_model(input_dim: int, output_dim: int) -> Sequential:
    """Build the MLP classifier."""
    model = Sequential(
        [
            Input(shape=(input_dim,)),
            Dense(128, activation="relu"),
            Dropout(0.3),
            Dense(64, activation="relu"),
            Dropout(0.3),
            Dense(32, activation="relu"),
            Dense(output_dim, activation="softmax"),
        ]
    )

    model.compile(
        optimizer="adam",
        loss="sparse_categorical_crossentropy",
        metrics=["accuracy"],
    )

    return model


def train_model(model: Sequential, X_train, y_train, X_test, y_test):
    """Train the model with validation output."""
    return model.fit(
        X_train,
        y_train,
        epochs=50,
        batch_size=32,
        validation_data=(X_test, y_test),
    )


def evaluate_model(model: Sequential, X_test, y_test, label_encoder: LabelEncoder):
    """Print test accuracy and classification diagnostics."""
    test_loss, test_accuracy = model.evaluate(X_test, y_test, verbose=0)
    print(f"\nTest loss: {test_loss:.4f}")
    print(f"Test accuracy: {test_accuracy:.4f}")

    y_pred_probs = model.predict(X_test, verbose=0)
    y_pred = np.argmax(y_pred_probs, axis=1)

    labels = np.arange(len(label_encoder.classes_))

    print("\nClassification report:")
    print(
        classification_report(
            y_test,
            y_pred,
            labels=labels,
            target_names=label_encoder.classes_,
            zero_division=0,
        )
    )

    print("Confusion matrix:")
    print(confusion_matrix(y_test, y_pred, labels=labels))


def save_artifacts(model: Sequential, label_encoder: LabelEncoder):
    """Save the trained model and label classes."""
    MODEL_DIR.mkdir(parents=True, exist_ok=True)
    model.save(TRAINED_MODEL_PATH)
    np.save(LABEL_CLASSES_PATH, label_encoder.classes_)

    print(f"\nModel saved: {TRAINED_MODEL_PATH}")
    print(f"Label classes saved: {LABEL_CLASSES_PATH}")


def main():
    """Run the full MLP training pipeline."""
    try:
        dataset_path = get_dataset_path()
        df = load_dataset(dataset_path)
        X, y, label_encoder = split_features_and_labels(df)

        X_train, X_test, y_train, y_test = train_test_split(
            X,
            y,
            test_size=0.2,
            random_state=42,
            stratify=y,
        )

        print("=== Motion Fighter MLP Trainer ===")
        print(f"Dataset: {dataset_path}")
        print(f"Samples: {len(df)}")
        print(f"Features: {X.shape[1]}")
        print(f"Classes: {list(label_encoder.classes_)}")

        model = build_model(input_dim=X.shape[1], output_dim=len(label_encoder.classes_))
        model.summary()

        train_model(model, X_train, y_train, X_test, y_test)
        evaluate_model(model, X_test, y_test, label_encoder)
        save_artifacts(model, label_encoder)

    except (FileNotFoundError, ValueError) as exc:
        raise SystemExit(f"\n[ERROR] {exc}") from exc


if __name__ == "__main__":
    main()
