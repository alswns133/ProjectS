using UnityEngine;

/// <summary>라이트를 무작위 간격으로 켰다 껐다 해 깜빡임을 연출한다.</summary>
public class LightFlickerRandom : MonoBehaviour
{
    /// <summary>깜빡일 라이트. 비워 두면 같은 오브젝트의 Light를 쓴다.</summary>
    public Light targetLight;

    /// <summary>한 번 켜짐/꺼짐이 유지되는 최소 시간(초).</summary>
    public float minDuration = 0.05f;

    /// <summary>한 번 켜짐/꺼짐이 유지되는 최대 시간(초).</summary>
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
