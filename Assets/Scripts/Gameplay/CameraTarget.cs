using UnityEngine;

public class CameraTarget : MonoBehaviour
{
    private Transform _target;

    void Update()
    {
        transform.position = _target.position;
    }

    public void SetTarget(Transform target)
    {
        _target = target;
    }
}