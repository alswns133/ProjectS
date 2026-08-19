using UnityEngine;

public class LightFlickerRandom : MonoBehaviour
{
    public Light targetLight;
    public float minDuration = 0.05f;
    public float maxDuration = 0.3f;

    float timer;
    float currentDuration;
    bool isOn = true;

    void Start()
    {
        if (targetLight == null)
            targetLight = GetComponent<Light>();

        currentDuration = Random.Range(minDuration, maxDuration);
    }

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= currentDuration)
        {
            isOn = !isOn;
            targetLight.enabled = isOn;
            currentDuration = Random.Range(minDuration, maxDuration);
            timer = 0f;
        }
    }
}
