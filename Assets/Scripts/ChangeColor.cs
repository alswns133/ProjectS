using UnityEngine;

public class ChangeColor : MonoBehaviour
{
    [SerializeField] private Color m_changeColor = Color.white; // 인스펙터에서 지정한 색. 자식 렌더러 전체를 이 색으로 덮어쓴다.
    private GameObject m_obj;
    private Renderer[] m_rnds;

    void Start()
    {
        m_obj = gameObject;
        m_rnds = m_obj.GetComponentsInChildren<Renderer>(true);

        // 셰이더마다 색 프로퍼티 이름이 달라, 대표적인 세 가지를 모두 세팅해 어떤 머티리얼이든 반영되게 한다.
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
