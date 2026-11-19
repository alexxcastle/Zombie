using UnityEngine;

public sealed class ElderboomRecoil
{
    public Vector3 position;
    public Vector3 rotation;
    Vector3 positionVelocity;
    Vector3 rotationVelocity;
    const float Frequency = 28f;
    const float Damping = 0.82f;
    static readonly Vector3 KickPosition = new Vector3(0f, 0.004f, -0.055f);
    static readonly Vector3 KickRotation = new Vector3(-0.025f, 0.007f, 0.004f);

    public void Kick(float scale)
    {
        positionVelocity += (KickPosition + new Vector3(Random.Range(-0.0015f, 0.0015f), 0f, 0f)) * (Frequency * 2.8f * scale);
        rotationVelocity += new Vector3(KickRotation.x, Random.Range(-KickRotation.y, KickRotation.y), Random.Range(-KickRotation.z, KickRotation.z)) * (Frequency * 2.8f * scale);
    }

    public void Advance(float delta)
    {
        float remaining = Mathf.Min(delta, 0.25f);
        while (remaining > 0f)
        {
            float step = Mathf.Min(remaining, 1f / 240f);
            positionVelocity += (-Frequency * Frequency * position - 2f * Damping * Frequency * positionVelocity) * step;
            rotationVelocity += (-Frequency * Frequency * rotation - 2f * Damping * Frequency * rotationVelocity) * step;
            position += positionVelocity * step;
            rotation += rotationVelocity * step;
            remaining -= step;
        }
    }

    public void Reset()
    {
        position = Vector3.zero;
        rotation = Vector3.zero;
        positionVelocity = Vector3.zero;
        rotationVelocity = Vector3.zero;
    }

    public void Settle(float seconds)
    {
        Reset();
        Kick(1f);
        float elapsed = 0f;
        float peak = 0f;
        float best = 0f;
        while (elapsed < seconds)
        {
            Advance(1f / 240f);
            elapsed += 1f / 240f;
            float amount = position.magnitude;
            if (amount > peak)
            {
                peak = amount;
                best = elapsed;
            }
        }
        Reset();
        Kick(1f);
        Advance(best);
    }
}
