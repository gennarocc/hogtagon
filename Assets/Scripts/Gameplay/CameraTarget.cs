using System;
using UnityEngine;

public class CameraTarget : MonoBehaviour
{
    [SerializeField][Range(10f, 50f)] public float lerpSpeed = 50f;
    private Transform _target;

    void Update()
    {
        if (_target == null) return;
        transform.position = Vector3.Lerp(transform.position, _target.position, Time.deltaTime * lerpSpeed);
    }

    public void SetTarget(Transform target)
    {
        _target = target;
    }
}