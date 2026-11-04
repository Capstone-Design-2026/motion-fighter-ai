"""Small causal TCN and unidirectional GRU with identical observable inputs."""
import numpy as np
import tensorflow as tf


@tf.keras.utils.register_keras_serializable(package='vision_v2')
class ObservedGate(tf.keras.layers.Layer):
    def call(self, inputs):
        values, observed = inputs
        return tf.where(observed[..., None], values, tf.zeros_like(values))


@tf.keras.utils.register_keras_serializable(package='vision_v2')
class FeatureGate(tf.keras.layers.Layer):
    def call(self, inputs):
        values, validity, observed = inputs
        valid = validity & observed[..., None]
        return tf.concat([tf.where(valid, values, tf.zeros_like(values)),
                          tf.cast(valid, values.dtype)], axis=-1)


@tf.keras.utils.register_keras_serializable(package='vision_v2')
class MaskedMean(tf.keras.layers.Layer):
    def call(self, inputs):
        values, observed = inputs
        clean = tf.where(observed[..., None], values, tf.zeros_like(values))
        count = tf.reduce_sum(tf.cast(observed, values.dtype), axis=1, keepdims=True)
        return tf.math.divide_no_nan(tf.reduce_sum(clean, axis=1), count)


def elapsed_observed(observed, grid_seconds):
    """Seconds since previous observed sample, zero for first/missing samples."""
    grid = np.asarray(grid_seconds)
    if grid.ndim != 1 or observed.shape[1] != len(grid) or not np.isfinite(grid).all() or np.any(np.diff(grid) <= 0):
        raise ValueError('Invalid sampling grid')
    out = np.zeros((*observed.shape, 1), dtype=np.float32)
    for row, valid in enumerate(observed):
        indices = np.flatnonzero(valid)
        out[row, indices[1:], 0] = np.diff(grid[indices])
    return out


def prepare_inputs(scaled, validity, observed, grid):
    if scaled.shape != validity.shape or observed.shape != scaled.shape[:2]:
        raise ValueError('Mask dimensions do not match')
    if validity.dtype != np.bool_ or observed.dtype != np.bool_:
        raise ValueError('Masks must be boolean')
    if np.any(validity & ~observed[..., None]) or not observed.any(axis=1).all():
        raise ValueError('Invalid observation mask or empty Trial')
    if not np.isfinite(scaled).all():
        raise ValueError('Nonfinite standardized values')
    return {'values': np.where(validity, scaled, 0).astype(np.float32),
            'validity': validity, 'observed': observed,
            'elapsed': elapsed_observed(observed, grid)}


def build_temporal(kind, learning_rate=.001, dropout=.2, l2=.0001):
    values = tf.keras.Input((None, 47), name='values')
    validity = tf.keras.Input((None, 47), dtype='bool', name='validity')
    observed = tf.keras.Input((None,), dtype='bool', name='observed')
    elapsed = tf.keras.Input((None, 1), name='elapsed')
    packed = FeatureGate()([values, validity, observed])
    packed = tf.keras.layers.Concatenate()([packed, ObservedGate()([elapsed, observed])])
    regularizer = tf.keras.regularizers.l2(l2)
    if kind == 'tcn':
        x = tf.keras.layers.Conv1D(16, 1, kernel_regularizer=regularizer)(packed)
        x = ObservedGate()([x, observed])
        for dilation in (1, 2, 4):
            residual = x
            for _ in range(2):
                x = tf.keras.layers.Conv1D(16, 3, padding='causal', dilation_rate=dilation,
                                           activation='relu', kernel_regularizer=regularizer)(x)
                x = tf.keras.layers.Dropout(dropout)(x)
                x = ObservedGate()([x, observed])
            x = tf.keras.layers.Add()([residual, x])
            x = ObservedGate()([x, observed])
    elif kind == 'gru':
        x = tf.keras.layers.GRU(24, return_sequences=True, dropout=dropout,
                                kernel_regularizer=regularizer, recurrent_regularizer=regularizer,
                                reset_after=True, zero_output_for_mask=True)(packed, mask=observed)
    else:
        raise ValueError('Expected tcn or gru')
    x = ObservedGate(name='temporal_features')([x, observed])
    pooled = MaskedMean()([x, observed])
    pooled = tf.keras.layers.Dropout(dropout)(pooled)
    predictions = tf.keras.layers.Dense(9, activation='softmax')(pooled)
    model = tf.keras.Model({'values':values, 'validity':validity, 'observed':observed, 'elapsed':elapsed}, predictions)
    model.compile(optimizer=tf.keras.optimizers.Adam(learning_rate), loss='sparse_categorical_crossentropy')
    return model
