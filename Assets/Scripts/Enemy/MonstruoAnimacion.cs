using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(BlindEnemyAI), typeof(NavMeshAgent))]
public class MonstruoAnimacion : MonoBehaviour
{
    public Animator animador;
    public float suavizado = 0.1f;
    [Tooltip("Length del clip de rugido (selecciona el clip en Project)")]
    public float duracionRugido = 2.5f;

    private BlindEnemyAI ai;
    private NavMeshAgent agent;
    private BlindEnemyAI.State anterior;

    private static readonly int Speed = Animator.StringToHash("Speed");
    private static readonly int Estado = Animator.StringToHash("Estado");
    private static readonly int Grito = Animator.StringToHash("Grito");

    private void Awake()
    {
        ai = GetComponent<BlindEnemyAI>();
        agent = GetComponent<NavMeshAgent>();
        if (animador == null) animador = GetComponentInChildren<Animator>();
        anterior = ai.Current;
    }

    private void Update()
    {
        if (animador == null) return;

        float vel = agent.velocity.magnitude;
        if (ai.Roaring || vel < 0.3f) vel = 0f;

        animador.SetFloat(Speed, vel, suavizado, Time.deltaTime);
        animador.SetInteger(Estado, (int)ai.Current);

        if (ai.Current != anterior)
        {
            // Ruge al empezar a cazar, pero no tras un ataque (Attack -> Hunt)
            if (ai.Current == BlindEnemyAI.State.Hunt && anterior != BlindEnemyAI.State.Attack)
            {
                ai.Roar(duracionRugido);
                animador.SetTrigger(Grito);
            }
            anterior = ai.Current;
        }
    }
}