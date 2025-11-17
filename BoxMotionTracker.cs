using UnityEngine;
using System.Collections.Generic;

public class BoxMotionTracker : MonoBehaviour
{
    public float loggingInterval = 0.1f; // Time between samples

    private Rigidbody rb;
    private float timer = 0f;
    private Vector3 previousVelocity;

    public List<float> timeStamps = new List<float>();
    public List<Vector3> velocityOverTime = new List<Vector3>();
    public List<Vector3> accelerationOverTime = new List<Vector3>();

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        previousVelocity = rb.velocity;
    }

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= loggingInterval)
        {
            float currentTime = Time.time;
            Vector3 currentVelocity = rb.velocity;
            Vector3 acceleration = (currentVelocity - previousVelocity) / loggingInterval;

            // Save data
            timeStamps.Add(currentTime);
            velocityOverTime.Add(currentVelocity);
            accelerationOverTime.Add(acceleration);

            // Update for next frame
            previousVelocity = currentVelocity;
            timer = 0f;
        }
    }
}
