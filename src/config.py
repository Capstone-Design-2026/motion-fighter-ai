from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent.parent

# MediaPipe model
POSE_MODEL_PATH = BASE_DIR / "pose_landmarker.task"

# Directories
DATA_DIR = BASE_DIR / "data"
RAW_DATA_DIR = DATA_DIR / "raw"
MERGED_DATA_DIR = DATA_DIR / "merged"
MODEL_DIR = BASE_DIR / "models"

# Files
RAW_CSV_PATH = RAW_DATA_DIR / "motion_data.csv"
MERGED_CSV_PATH = MERGED_DATA_DIR / "motion_data_merged.csv"
TRAINED_MODEL_PATH = MODEL_DIR / "motion_model.keras"
LABEL_CLASSES_PATH = MODEL_DIR / "label_classes.npy"

# Action labels
ACTIONS = [
    "IDLE",
    "NONE",
    "ATTACK",
    "BLOCK",
    "JUMP",
    "CROUCH",
]

# Data collection settings
CAMERA_INDEX = 0
COUNTDOWN_SECONDS = 3
COLLECT_SECONDS = 5

# Inference settings
CONFIDENCE_THRESHOLD = 0.70
STABLE_FRAME_COUNT = 5

# Unity UDP settings
UNITY_HOST = "127.0.0.1"
UNITY_PORT = 5055