using UnityEngine;

public class ChangeColor : MonoBehaviour
{
    [SerializeField] private Color m_changeColor = Color.white; // 인스펙터에서 색 지정
    private GameObject m_obj;
    private Renderer[] m_rnds;

    void Start()
    {
        m_obj = gameObject;
        m_rnds = m_obj.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer rend in m_rnds)
        {
            for (int i = 0; i < rend.materials.Length; i++)
            {
                rend.materials[i].SetColor("_TintColor", m_changeColor);
                rend.materials[i].SetColor("_Color", m_changeColor);
                rend.materials[i].SetColor("_RimColor", m_changeColor);
            }
        }
    }
}