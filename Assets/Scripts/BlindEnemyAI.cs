using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// Enemigo ciego que solo percibe por sonido.
///  - Oido realista: atenuacion por distancia y por paredes, y error de localizacion.
///  - Patrones: (1) prediccion de movimiento por cadencia de pasos, (2) zonas calientes
///    donde el jugador hace ruido, (3) aprendizaje de señuelos (objetos lanzados).
///  - Estados: Patrol, Investigate, Hunt, Search, Attack.
/// Requiere NavMesh (paquete AI Navigation) y un NavMeshAgent.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class BlindEnemyAI : MonoBehaviour
{
    enum State { Patrol, Investigate, Hunt, Search, Attack }

    class Heard { public Vector3 pos; public float time; public float intensity; public SoundType type; }
    class Hotspot { public float heat; public Vector3 center; }

    [Header("Cautela (estilo A Quiet Place)")]
    public Vector2 moveBurst = new Vector2(1.5f, 3f);   // seg. avanzando
    public Vector2 pauseTime = new Vector2(1f, 2.5f);   // seg. quieto escuchando
    public float huntFreshSound = 1.5f;                 // si no oye nada en este tiempo, baja el ritmo
    float creepTimer;
    bool creepPaused;

    [Header("Referencias")]
    public Transform ears;
    public Transform player;                 // solo se usa para el contacto/ataque
    public Transform[] patrolPoints;         // opcional; si esta vacio, vaga por el NavMesh

    [Header("Oido")]
    public float hearingSensitivity = 1f;
    public LayerMask obstacleMask = ~0;      // capas que bloquean el sonido (paredes)
    [Range(0f, 0.95f)] public float wallAttenuation = 0.45f;
    public float maxLocalizationError = 4f;
    public float minIntensity = 0.05f;

    [Header("Alerta")]
    public float alertGain = 1.2f;
    public float alertDecay = 0.12f;
    public float investigateThreshold = 0.15f;
    public float huntThreshold = 0.55f;
    public float huntMemory = 3.5f;          // seg. sin oir nada antes de pasar a Search
    public float listenPause = 0.5f;         // se queda quieto un instante a escuchar
    public float listenCooldown = 3f;

    [Header("Velocidades")]
    public float patrolSpeed = 1.5f;
    public float investigateSpeed = 2.6f;
    public float huntSpeed = 4.6f;
    public float searchSpeed = 2.4f;

    [Header("Ataque")]
    public float attackRange = 1.6f;
    public float attackCooldown = 1.5f;
    public UnityEvent onAttackPlayer;

    [Header("Patrones / aprendizaje")]
    public float hotspotCellSize = 6f;
    public float hotspotHalfLife = 120f;
    [Range(0f, 1f)] public float patrolHotspotBias = 0.5f;
    public float predictionLead = 0.8f;
    public float maxPredictDistance = 8f;
    public float decoyConfirmRadius = 3.5f;
    public float credibilityLoss = 0.3f;
    public float credibilityGain = 0.15f;
    public float credibilityRecoveryPerSec = 0.01f;
    public float searchRadius = 8f;
    public float searchWait = 1.5f;

    NavMeshAgent agent;
    State state;

    // memoria
    readonly List<Heard> memory = new();
    readonly List<Heard> steps = new();
    readonly Dictionary<Vector2Int, Hotspot> hotspots = new();
    readonly List<Vector2Int> removeBuffer = new();
    float[] credibility;

    // estado de percepcion
    float alert;
    Vector3 lastHeardPos;
    float lastHeardTime;
    Vector3 predictedVelocity;
    float predictionConfidence;

    // estado de comportamiento
    Vector3 investigatePos;
    SoundType investigateType;
    float listenTimer, nextListenAllowed, attackTimer, waitTimer, repathTimer;
    bool hasTarget;
    readonly Queue<Vector3> searchQueue = new();
    readonly RaycastHit[] hitBuffer = new RaycastHit[8];

    static bool IsPlayerLike(SoundType t) => t == SoundType.Footstep || t == SoundType.Run;
    static bool IsHuntable(SoundType t) => t == SoundType.Footstep || t == SoundType.Run || t == SoundType.Voice;
    static bool IsLearnable(SoundType t) => t == SoundType.Thrown || t == SoundType.Impact || t == SoundType.Other;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        credibility = new float[System.Enum.GetValues(typeof(SoundType)).Length];
        for (int i = 0; i < credibility.Length; i++) credibility[i] = 1f;
        if (!ears) ears = transform;
    }

    void OnEnable() => SoundBus.OnSound += HandleSound;
    void OnDisable() => SoundBus.OnSound -= HandleSound;

    void Start() => SetState(State.Patrol);

    // ------------------------------------------------------------------ OIDO
    void HandleSound(SoundEvent e)
    {
        if (e.source == gameObject) return;

        Vector3 earPos = ears.position;
        float dist = Vector3.Distance(earPos, e.position);
        float radius = e.loudness * hearingSensitivity * Occlusion(earPos, e.position);
        if (radius <= 0.01f || dist > radius) return;

        // Cuanto mas lejos/tapado, menos claro y peor localizado
        float clarity = Mathf.Sqrt(1f - dist / radius);
        Vector2 err = Random.insideUnitCircle * ((1f - clarity) * maxLocalizationError);
        Vector3 heardPos = e.position + new Vector3(err.x, 0f, err.y);

        float intensity = clarity * credibility[(int)e.type];
        if (intensity < minIntensity) return;

        Register(heardPos, intensity, e.type);
        if (state == State.Attack) return;

        bool wasCalm = state == State.Patrol || state == State.Search;

        if (alert >= huntThreshold && IsHuntable(e.type))
        {
            if (state != State.Hunt) SetState(State.Hunt);
        }
        else if (alert >= investigateThreshold && state != State.Hunt)
        {
            BeginInvestigate(heardPos, e.type);
        }

        if (wasCalm && listenTimer <= 0f && Time.time >= nextListenAllowed)
        {
            listenTimer = listenPause;
            nextListenAllowed = Time.time + listenCooldown;
        }
    }

    float Occlusion(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float d = dir.magnitude;
        if (d < 0.01f) return 1f;
        int hits = Physics.RaycastNonAlloc(from, dir / d, hitBuffer, d, obstacleMask, QueryTriggerInteraction.Ignore);
        int walls = 0;
        for (int i = 0; i < hits; i++)
        {
            if (hitBuffer[i].transform.IsChildOf(transform)) continue;
            if (player && hitBuffer[i].transform.IsChildOf(player)) continue;
            walls++;
        }
        return Mathf.Pow(1f - wallAttenuation, walls);
    }

    void Register(Vector3 pos, float intensity, SoundType type)
    {
        var h = new Heard { pos = pos, time = Time.time, intensity = intensity, type = type };
        memory.Add(h);
        if (memory.Count > 24) memory.RemoveAt(0);

        lastHeardPos = pos;
        lastHeardTime = Time.time;
        alert = Mathf.Clamp01(alert + intensity * alertGain);

        // PATRON 2: zonas calientes
        var cell = new Vector2Int(Mathf.RoundToInt(pos.x / hotspotCellSize), Mathf.RoundToInt(pos.z / hotspotCellSize));
        if (!hotspots.TryGetValue(cell, out var hs)) { hs = new Hotspot { center = pos }; hotspots[cell] = hs; }
        hs.center = Vector3.Lerp(hs.center, pos, intensity / (hs.heat + intensity));
        hs.heat += intensity;

        // PATRON 1: cadencia de pasos
        if (IsPlayerLike(type))
        {
            steps.Add(h);
            while (steps.Count > 0 && steps[0].time < Time.time - 4f) steps.RemoveAt(0);
            if (steps.Count > 6) steps.RemoveAt(0);
            UpdatePrediction();
        }
    }

    // ------------------------------------------------------------- PATRONES
    void UpdatePrediction()
    {
        predictionConfidence = 0f;
        if (steps.Count < 3) return;

        Heard a = steps[0], b = steps[steps.Count - 1];
        float dt = b.time - a.time;
        if (dt < 0.3f) return;

        Vector3 v = (b.pos - a.pos) / dt;
        v.y = 0f;
        predictedVelocity = v;

        // regularidad del ritmo: coeficiente de variacion de los intervalos
        float mean = dt / (steps.Count - 1), var = 0f;
        for (int i = 1; i < steps.Count; i++)
        {
            float d = (steps[i].time - steps[i - 1].time) - mean;
            var += d * d;
        }
        float cv = Mathf.Sqrt(var / (steps.Count - 1)) / Mathf.Max(mean, 0.01f);
        predictionConfidence = Mathf.Clamp01(1f - cv) * Mathf.Clamp01(v.magnitude / 1.2f);
    }

    Vector3 PredictedPosition()
    {
        Heard last = steps[steps.Count - 1];
        float elapsed = Time.time - last.time + predictionLead;
        Vector3 offset = predictedVelocity.normalized * Mathf.Min(predictedVelocity.magnitude * elapsed, maxPredictDistance);
        return last.pos + offset;
    }

    bool CanPredict => predictionConfidence >= 0.35f && steps.Count > 0 && Time.time - steps[steps.Count - 1].time < 2f;

    void DecayHeat(float dt)
    {
        if (hotspots.Count == 0) return;
        float f = Mathf.Pow(0.5f, dt / hotspotHalfLife);
        removeBuffer.Clear();
        foreach (var kv in hotspots)
        {
            kv.Value.heat *= f;
            if (kv.Value.heat < 0.1f) removeBuffer.Add(kv.Key);
        }
        foreach (var k in removeBuffer) hotspots.Remove(k);
    }

    List<Hotspot> TopHotspots(int count, Vector3 near, float maxDist)
    {
        var list = new List<Hotspot>();
        foreach (var kv in hotspots)
            if (kv.Value.heat > 0.8f && Vector3.Distance(kv.Value.center, near) <= maxDist) list.Add(kv.Value);
        list.Sort((x, y) => y.heat.CompareTo(x.heat));
        if (list.Count > count) list.RemoveRange(count, list.Count - count);
        return list;
    }

    // --------------------------------------------------------- COMPORTAMIENTO
    void Update()
    {
        float dt = Time.deltaTime;
        alert = Mathf.Max(0f, alert - alertDecay * dt);
        DecayHeat(dt);
        for (int i = 0; i < credibility.Length; i++)
            credibility[i] = Mathf.Min(1f, credibility[i] + credibilityRecoveryPerSec * dt);

        // Ataque por contacto (es ciego: solo ataca si lo tiene encima)
        if (state == State.Attack)
        {
            attackTimer -= dt;
            if (attackTimer <= 0f)
            {
                agent.isStopped = false;
                if (player) { lastHeardPos = player.position; lastHeardTime = Time.time; }
                SetState(State.Hunt);
            }
            return;
        }

        if (player && Vector3.Distance(transform.position, player.position) <= attackRange)
        {
            SetState(State.Attack);
            return;
        }
        if (player && Vector3.Distance(transform.position, player.position) <= attackRange)
        {
            SetState(State.Attack);
            return;
        }

        // Escucha: se queda quieto y gira hacia el ultimo sonido
        if (listenTimer > 0f)
        {
            listenTimer -= dt;
            agent.isStopped = true;
            FaceTowards(lastHeardPos, dt);
            if (listenTimer <= 0f) agent.isStopped = false;
            return;
        }
        if (state != State.Hunt)
        {
            TickCreep(dt);
            agent.isStopped = creepPaused;
            if (creepPaused) { FaceTowards(lastHeardPos, dt); return; }
        }

        switch (state)
        {
            case State.Patrol: TickPatrol(dt); break;
            case State.Investigate: TickInvestigate(); break;
            case State.Hunt: TickHunt(dt); break;
            case State.Search: TickSearch(dt); break;
        }
    }

    void SetState(State s)
    {
        state = s;
        hasTarget = false;
        waitTimer = 0f;
        agent.isStopped = false;
        switch (s)
        {
            case State.Patrol: agent.speed = patrolSpeed; break;
            case State.Investigate: agent.speed = investigateSpeed; break;
            case State.Hunt: agent.speed = huntSpeed; repathTimer = 0f; break;
            case State.Search: agent.speed = searchSpeed; break;
            case State.Attack:
                attackTimer = attackCooldown;
                agent.isStopped = true;
                agent.ResetPath();
                onAttackPlayer?.Invoke();
                break;
        }
    }

    void TickPatrol(float dt)
    {
        if (waitTimer > 0f) { waitTimer -= dt; return; }
        if (!hasTarget)
        {
            hasTarget = SetDest(PickPatrolTarget());
            return;
        }
        if (HasArrived()) { hasTarget = false; waitTimer = Random.Range(1f, 2.5f); }
    }

    Vector3 PickPatrolTarget()
    {
        // PATRON 2: a veces patrulla donde el jugador hizo mas ruido
        if (Random.value < patrolHotspotBias)
        {
            var top = TopHotspots(3, transform.position, 60f);
            if (top.Count > 0) return top[Random.Range(0, top.Count)].center;
        }
        if (patrolPoints != null && patrolPoints.Length > 0)
            return patrolPoints[Random.Range(0, patrolPoints.Length)].position;
        return transform.position + Random.insideUnitSphere * 15f;
    }

    void BeginInvestigate(Vector3 pos, SoundType type)
    {
        investigatePos = pos;
        investigateType = type;
        if (state != State.Investigate) SetState(State.Investigate);
        hasTarget = SetDest(pos);
    }

    void TickInvestigate()
    {
        if (!hasTarget) { hasTarget = SetDest(investigatePos); return; }
        if (!HasArrived()) return;

        bool playerNear = player && Vector3.Distance(player.position, investigatePos) <= decoyConfirmRadius;

        // PATRON 3: aprende que sonidos son señuelos
        if (IsLearnable(investigateType))
        {
            int i = (int)investigateType;
            credibility[i] = playerNear
                ? Mathf.Min(1f, credibility[i] + credibilityGain)
                : Mathf.Max(0.1f, credibility[i] - credibilityLoss);
        }

        if (playerNear)
        {
            if (player) { lastHeardPos = player.position; lastHeardTime = Time.time; }
            alert = 1f;
            SetState(State.Hunt);
        }
        else
        {
            BuildSearchQueue(2);
            SetState(State.Search);
        }
    }

    void TickHunt(float dt)
    {
        if (Time.time - lastHeardTime > huntMemory)
        {
            BuildSearchQueue(3);
            SetState(State.Search);
            return;
        }
        repathTimer -= dt;
        if (repathTimer > 0f) return;
        repathTimer = 0.25f;
        SetDest(CanPredict ? PredictedPosition() : lastHeardPos);
    }

    void BuildSearchQueue(int hotspotCount)
    {
        searchQueue.Clear();
        if (steps.Count > 0 && predictionConfidence > 0.2f) searchQueue.Enqueue(PredictedPosition());
        foreach (var h in TopHotspots(hotspotCount, lastHeardPos, 30f)) searchQueue.Enqueue(h.center);
        for (int i = 0; i < 2; i++)
        {
            Vector2 c = Random.insideUnitCircle * searchRadius;
            searchQueue.Enqueue(lastHeardPos + new Vector3(c.x, 0f, c.y));
        }
    }

    void TickSearch(float dt)
    {
        if (waitTimer > 0f) { waitTimer -= dt; return; }
        if (!hasTarget)
        {
            if (searchQueue.Count == 0) { SetState(State.Patrol); return; }
            hasTarget = SetDest(searchQueue.Dequeue());
            return;
        }
        if (HasArrived()) { hasTarget = false; waitTimer = searchWait; }
    }

    // ----------------------------------------------------------------- UTILES
    bool SetDest(Vector3 p)
    {
        if (!NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas)) return false;
        return agent.SetDestination(hit.position);
    }

    bool HasArrived() => !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f;

    void FaceTowards(Vector3 worldPos, float dt)
    {
        Vector3 d = worldPos - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 6f * dt);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        foreach (var h in memory) Gizmos.DrawWireSphere(h.pos, 0.3f + h.intensity * 0.5f);

        Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.6f);
        foreach (var kv in hotspots) Gizmos.DrawWireSphere(kv.Value.center, Mathf.Clamp(kv.Value.heat * 0.4f, 0.5f, 4f));

        if (Application.isPlaying && steps.Count > 0 && predictionConfidence > 0.2f)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(PredictedPosition(), 0.4f);
        }
    }
    void TickCreep(float dt)
    {
        creepTimer -= dt;
        if (creepTimer > 0f) return;
        creepPaused = !creepPaused;
        creepTimer = Random.Range(creepPaused ? pauseTime.x : moveBurst.x,
                                  creepPaused ? pauseTime.y : moveBurst.y);
    }
}
