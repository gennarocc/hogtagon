using UnityEngine;

/// <summary>
/// Static utility class for deterministic physics calculations
/// Provides methods to quantize floating-point values to ensure consistent results across platforms
/// </summary>
public static class PhysicsQuantizer
{
    // Default precision settings (can be adjusted in a configuration file)
    private static int _defaultPrecision = 1000;   // 3 decimal places
    private static int _inputPrecision = 100;      // 2 decimal places
    private static int _highPrecision = 10000;     // 4 decimal places
    
    // Enable/disable quantization globally (for debugging/testing)
    private static bool _quantizationEnabled = true;
    
    /// <summary>
    /// Configure the quantizer settings
    /// </summary>
    public static void Configure(int defaultPrecision = 1000, int inputPrecision = 100, 
                                int highPrecision = 10000, bool enabled = true)
    {
        _defaultPrecision = defaultPrecision;
        _inputPrecision = inputPrecision;
        _highPrecision = highPrecision;
        _quantizationEnabled = enabled;
    }
    
    #region Float Quantization
    
    /// <summary>
    /// Quantize a float value to a specific precision
    /// </summary>
    /// <param name="value">The float value to quantize</param>
    /// <param name="precision">Precision multiplier (1000 = 3 decimal places)</param>
    /// <returns>Quantized float value</returns>
    public static float QFloat(float value, int precision = -1)
    {
        if (!_quantizationEnabled) return value;
        
        if (precision < 0) precision = _defaultPrecision;
        return Mathf.Round(value * precision) / precision;
    }
    
    /// <summary>
    /// Quantize an input value (lower precision for controls)
    /// </summary>
    public static float QInput(float value)
    {
        return QFloat(value, _inputPrecision);
    }
    
    /// <summary>
    /// Quantize a value with high precision (for critical calculations)
    /// </summary>
    public static float QHigh(float value)
    {
        return QFloat(value, _highPrecision);
    }
    
    #endregion
    
    #region Vector Quantization
    
    /// <summary>
    /// Quantize a Vector2 to ensure deterministic calculations
    /// </summary>
    public static Vector2 QVector2(Vector2 vector, int precision = -1)
    {
        if (!_quantizationEnabled) return vector;
        
        return new Vector2(
            QFloat(vector.x, precision),
            QFloat(vector.y, precision)
        );
    }
    
    /// <summary>
    /// Quantize a Vector3 to ensure deterministic calculations
    /// </summary>
    public static Vector3 QVector3(Vector3 vector, int precision = -1)
    {
        if (!_quantizationEnabled) return vector;
        
        return new Vector3(
            QFloat(vector.x, precision),
            QFloat(vector.y, precision),
            QFloat(vector.z, precision)
        );
    }
    
    /// <summary>
    /// Quantize a Vector3 and normalize it deterministically
    /// </summary>
    public static Vector3 QNormalize(Vector3 vector, int precision = -1)
    {
        if (!_quantizationEnabled) return vector.normalized;
        
        Vector3 quantized = QVector3(vector, precision);
        float magnitude = QFloat(quantized.magnitude, precision);
        
        if (magnitude > 0.00001f)
        {
            return QVector3(quantized / magnitude, precision);
        }
        
        return Vector3.zero;
    }
    
    /// <summary>
    /// Quantize a position vector with high precision
    /// </summary>
    public static Vector3 QPosition(Vector3 position)
    {
        return QVector3(position, _highPrecision);
    }
    
    /// <summary>
    /// Quantize a direction/normal vector and normalize it
    /// </summary>
    public static Vector3 QDirection(Vector3 direction)
    {
        return QNormalize(direction, _defaultPrecision);
    }
    
    #endregion
    
    #region Quaternion Quantization
    
    /// <summary>
    /// Quantize a quaternion for deterministic rotations
    /// </summary>
    public static Quaternion QQuaternion(Quaternion rotation, int precision = -1)
    {
        if (!_quantizationEnabled) return rotation;
        
        return new Quaternion(
            QFloat(rotation.x, precision),
            QFloat(rotation.y, precision),
            QFloat(rotation.z, precision),
            QFloat(rotation.w, precision)
        ).normalized;
    }
    
    #endregion
    
    #region Deterministic Math Operations
    
    /// <summary>
    /// Deterministic dot product
    /// </summary>
    public static float QDot(Vector3 a, Vector3 b, int precision = -1)
    {
        if (!_quantizationEnabled) return Vector3.Dot(a, b);
        
        Vector3 qa = QVector3(a, precision);
        Vector3 qb = QVector3(b, precision);
        
        return QFloat(qa.x * qb.x + qa.y * qb.y + qa.z * qb.z, precision);
    }
    
    /// <summary>
    /// Deterministic cross product
    /// </summary>
    public static Vector3 QCross(Vector3 a, Vector3 b, int precision = -1)
    {
        if (!_quantizationEnabled) return Vector3.Cross(a, b);
        
        Vector3 qa = QVector3(a, precision);
        Vector3 qb = QVector3(b, precision);
        
        return QVector3(new Vector3(
            qa.y * qb.z - qa.z * qb.y,
            qa.z * qb.x - qa.x * qb.z,
            qa.x * qb.y - qa.y * qb.x
        ), precision);
    }
    
    /// <summary>
    /// Interpolate between two values deterministically
    /// </summary>
    public static float QLerp(float a, float b, float t, int precision = -1)
    {
        if (!_quantizationEnabled) return Mathf.Lerp(a, b, t);
        
        float qt = QFloat(Mathf.Clamp01(t), precision);
        return QFloat(a * (1f - qt) + b * qt, precision);
    }
    
    /// <summary>
    /// Interpolate between two vectors deterministically
    /// </summary>
    public static Vector3 QLerp(Vector3 a, Vector3 b, float t, int precision = -1)
    {
        if (!_quantizationEnabled) return Vector3.Lerp(a, b, t);
        
        float qt = QFloat(Mathf.Clamp01(t), precision);
        return QVector3(a * (1f - qt) + b * qt, precision);
    }
    
    #endregion
    
    #region Physics Helpers
    
    /// <summary>
    /// Apply a force to a rigidbody deterministically
    /// </summary>
    public static void QAddForce(Rigidbody rb, Vector3 force, ForceMode mode = ForceMode.Force)
    {
        rb.AddForce(QVector3(force), mode);
    }
    
    /// <summary>
    /// Apply a force at position deterministically
    /// </summary>
    public static void QAddForceAtPosition(Rigidbody rb, Vector3 force, Vector3 position, 
                                          ForceMode mode = ForceMode.Force)
    {
        rb.AddForceAtPosition(QVector3(force), QVector3(position), mode);
    }
    
    /// <summary>
    /// Perform a deterministic raycast (wraps Physics.Raycast)
    /// </summary>
    public static bool QRaycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, 
                               float maxDistance = Mathf.Infinity, int layerMask = Physics.DefaultRaycastLayers)
    {
        Vector3 qOrigin = QPosition(origin);
        Vector3 qDirection = QDirection(direction);
        float qDistance = QFloat(maxDistance);
        
        bool hit = Physics.Raycast(qOrigin, qDirection, out hitInfo, qDistance, layerMask);
        
        if (hit)
        {
            // Quantize the hit results
            hitInfo.point = QPosition(hitInfo.point);
            hitInfo.normal = QDirection(hitInfo.normal);
            hitInfo.distance = QFloat(hitInfo.distance);
        }
        
        return hit;
    }
    
    #endregion
}