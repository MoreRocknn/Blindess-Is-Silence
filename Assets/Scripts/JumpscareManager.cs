using System.Collections;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class JumpscareManager : MonoBehaviour
{
    [Header("Screamer")]
    public GameObject screamerCanvas;      // Canvas con una Image a pantalla completa, desactivado
    public AudioSource audioSource;
    public AudioClip screamClip;
    public RectTransform screamerImage;    // opcional: para el temblor
    public float shakeAmount = 15f;

    [Header("Muerte")]
    public float duration = 2.2f;
    public bool reloadScene = true;

    bool triggered;

    public void Trigger()                  // enlazar a onAttackPlayer
    {
        if (triggered) return;
        triggered = true;
        StartCoroutine(Routine());
    }

    IEnumerator Routine()
    {
        var fpc = FindFirstObjectByType<FirstPersonController>();
        if (fpc)
        {
            fpc.enabled = false;
            var pi = fpc.GetComponent<PlayerInput>();
            if (pi) pi.enabled = false;
        }

        if (screamerCanvas) screamerCanvas.SetActive(true);
        if (audioSource && screamClip) audioSource.PlayOneShot(screamClip);

        Vector2 basePos = screamerImage ? screamerImage.anchoredPosition : Vector2.zero;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            if (screamerImage)
                screamerImage.anchoredPosition = basePos + Random.insideUnitCircle * shakeAmount;
            yield return null;
        }

        if (reloadScene)
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}