using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// Enemigo ciego que solo percibe por sonido.
///  - Oido realista: atenuacion por distancia y por paredes, y error de localizacion.
///  - Patrones: (1) prediccion de movimiento por regresion ponderada de pasos,
///    (2) zonas calientes donde el jugador hace ruido (con enfriamiento al visitarlas en vano),
///    (3) aprendizaje de señuelos (objetos lanzados).
///  - Estados: Patrol, Investigate, Hunt, Search, Attack.
/// Requiere NavMesh (paquete AI Navigation) y un NavMeshAgent.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class BlindEnemyAI : MonoBehaviour
{
    public enum State { Patrol, Investigate, Hunt, Search, Attack }

    // struct: no genera basura (GC) por cada sonido que oye
    struct Heard { public Vector3 pos; public float time; public float intensity; public SoundType type; }
    class Hotspot { public float heat; public Vector3 center; }

    [Header("Cautela (estilo A Quiet Place)")]
    public Vector2 moveBurst = new Vector2(1.5f, 3f);   // seg. avanzando
    public Vector2 pauseTime = new Vector2(1f, 2.5f);   // seg. quieto escuchando
    public float huntFreshSound = 1.5f;                 // sin oir nada en este tiempo, en Hunt baja el ritmo
    float creepTimer;
    bool creepPaused;


    [Header("Referencias")]
    public Transform ears;
    public Transform player;                 // solo se usa para el contacto/ataque
    [Tooltip("PasosPersonaje del jugador. Vacio = se busca en 'player'.")]
    public PasosPersonaje playerNoise;
    public Transform[] patrolPoints;         // opcional; si esta vacio, vaga por el NavMesh

    [Header("Oido")]
    public float hearingSensitivity = 1f;
    public LayerMask obstacleMask = ~0;      // capas que bloquean el sonido (paredes)
    [Range(0f, 0.95f)] public float wallAttenuation = 0.45f;
    public float maxLocalizationError = 4f;
    public float minIntensity = 0.05f;
    [Tooltip("Oye al jugador directamente segun lo que hace (correr / caminar / agachado). Desmarcalo si ya emites sus pasos por SoundBus.")]
    public bool hearPlayerDirectly = true;
    [Tooltip("Metros a los que se oye al jugador corriendo (ruido = 1). Caminar y agachado usan su parte de este valor.")]
    public float playerHearingRadius = 20f;

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
    float roarTimer;
    public bool Roaring => roarTimer > 0f;

    [Header("Patrones / aprendizaje")]
    public float hotspotCellSize = 6f;
    public float hotspotHalfLife = 120f;
    [Range(0f, 1f)] public float patrolHotspotBias = 0.5f;
    [Range(0f, 1f)] public float hotspotCooling = 0.6f;   // multiplicador de calor al visitar una zona sin hallar nada
    public float predictionLead = 0.8f;
    public float maxPredictDistance = 8f;
    public float maxPlayerSpeed = 6f;                     // tope de velocidad estimada del jugador
    public float decoyConfirmRadius = 3.5f;
    public float credibilityLoss = 0.3f;
    public float credibilityGain = 0.15f;
    public float credibilityRecoveryPerSec = 0.01f;
    public float searchRadius = 8f;
    public float searchWait = 1.5f;

    [Header("Navegacion")]
    public float stuckTime = 2.5f;                        // seg. atascado antes de replanificar

    NavMeshAgent agent;
    State state;

    // memoria
    readonly List<Heard> memory = new();
    readonly List<Heard> steps = new();
    readonly Dictionary<Vector2Int, Hotspot> hotspots = new();
    readonly List<Vector2Int> removeBuffer = new();
    readonly List<Hotspot> hotBuffer = new();
    readonly List<Vector3> candidateBuffer = new();
    float[] credibility;

    // estado de percepcion
    float alert;
    Vector3 lastHeardPos;
    float lastHeardTime;
    Vector3 predictedVelocity;
    Vector3 predictionAnchor;
    float predictionAnchorTime;
    float predictionConfidence;

    // estado de comportamiento
    Vector3 investigatePos;
    SoundType investigateType;
    float investigateIntensity;
    Hotspot patrolHotspot;
    float listenTimer, nextListenAllowed, attackTimer, waitTimer, repathTimer, stuckTimer;
    bool hasTarget;
    readonly Queue<Vector3> searchQueue = new();
    readonly RaycastHit[] hitBuffer = new RaycastHit[8];
    float stepTimer, slowTimer;

    // Lo lee MonstruoAudio
    public State Current => state;
    public float Alert => alert;

    static bool IsPlayerLike(SoundType t) => t == SoundType.Footstep || t == SoundType.Run;
    static bool IsHuntable(SoundType t) => t == SoundType.Footstep || t == SoundType.Run || t == SoundType.Voice;
    static bool IsLearnable(SoundType t) => t == SoundType.Thrown || t == SoundType.Impact || t == SoundType.Other;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        credibility = new float[System.Enum.GetValues(typeof(SoundType)).Length];
        for (int i = 0; i < credibility.Length; i++) credibility[i] = 1f;
        if (!ears) ears = transform;
        if (!playerNoise && player) playerNoise = player.GetComponentInChildren<PasosPersonaje>();
    }

    void OnEnable() => SoundBus.OnSound += HandleSound;
    void OnDisable() => SoundBus.OnSound -= HandleSound;

    void Start() => SetState(State.Patrol);

    // ------------------------------------------------------------------ OIDO
    void HandleSound(SoundEvent e) => Hear(e.source, e.position, e.loudness, e.type);

    /// <summary>Nucleo del oido: lo usan los eventos de SoundBus y el oido directo al jugador.</summary>
    void Hear(UnityEngine.Object src, Vector3 position, float loudness, SoundType type)
    {
        if (src == gameObject) return;

        Vector3 earPos = ears.position;
        float dist = Vector3.Distance(earPos, position);
        float radius = loudness * hearingSensitivity;
        if (dist > radius) return;                       // descarte barato antes de lanzar el raycast

        radius *= Occlusion(earPos, position);
        if (radius <= 0.01f || dist > radius) return;

        // Cuanto mas lejos/tapado, menos claro y peor localizado
        float clarity = Mathf.Sqrt(1f - dist / radius);
        Vector2 err = Random.insideUnitCircle * ((1f - clarity) * maxLocalizationError);
        Vector3 heardPos = position + new Vector3(err.x, 0f, err.y);

        float intensity = clarity * credibility[(int)type];
        if (intensity < minIntensity) return;

        Register(heardPos, intensity, type);
        if (state == State.Attack) return;

        bool wasCalm = state == State.Patrol || state == State.Search;

        if (alert >= huntThreshold && IsHuntable(type))
        {
            if (state != State.Hunt) SetState(State.Hunt);
        }
        else if (alert >= investigateThreshold && state != State.Hunt)
        {
            // Evita cambiar de objetivo con cada sonido debil mientras ya investiga uno mas fuerte
            if (state != State.Investigate || intensity >= investigateIntensity * 0.7f)
                BeginInvestigate(heardPos, type, intensity);
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

        // PATRON 2: zonas calientes (los señuelos y ruidos ambiguos calientan menos)
        float heatGain = IsHuntable(type) ? intensity : intensity * 0.4f;
        var cell = new Vector2Int(Mathf.RoundToInt(pos.x / hotspotCellSize), Mathf.RoundToInt(pos.z / hotspotCellSize));
        if (!hotspots.TryGetValue(cell, out var hs)) { hs = new Hotspot { center = pos }; hotspots[cell] = hs; }
        hs.center = Vector3.Lerp(hs.center, pos, heatGain / (hs.heat + heatGain));
        hs.heat += heatGain;

        // PATRON 1: cadencia de pasos
        if (IsPlayerLike(type))
        {
            steps.Add(h);
            while (steps.Count > 0 && steps[0].time < Time.time - 4f) steps.RemoveAt(0);
            if (steps.Count > 8) steps.RemoveAt(0);
            UpdatePrediction();
        }
    }

    /// <summary>Conocimiento exacto (contacto fisico): corrige la memoria con una muestra fiable.</summary>
    void SetExactKnowledge(Vector3 pos)
    {
        lastHeardPos = pos;
        lastHeardTime = Time.time;
        memory.Add(new Heard { pos = pos, time = Time.time, intensity = 1f, type = SoundType.Footstep });
        if (memory.Count > 24) memory.RemoveAt(0);
    }

    // ------------------------------------------------------------- PATRONES
    /// <summary>
    /// Regresion lineal ponderada (por intensidad) de las posiciones oidas en el tiempo.
    /// Filtra el ruido de localizacion mucho mejor que usar solo primer y ultimo paso.
    /// </summary>
    void UpdatePrediction()
    {
        predictionConfidence = 0f;
        int n = steps.Count;
        if (n < 3) return;

        float span = steps[n - 1].time - steps[0].time;
        if (span < 0.3f) return;

        float t0 = steps[0].time;
        float wSum = 0f, tm = 0f;
        Vector3 pm = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            float w = steps[i].intensity + 0.05f;
            wSum += w;
            tm += w * (steps[i].time - t0);
            pm += w * steps[i].pos;
        }
        tm /= wSum;
        pm /= wSum;

        float stt = 0f;
        Vector3 stp = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            float w = steps[i].intensity + 0.05f;
            float dtv = (steps[i].time - t0) - tm;
            stt += w * dtv * dtv;
            stp += w * dtv * (steps[i].pos - pm);
        }
        if (stt < 0.05f) return;

        Vector3 v = stp / stt;
        v.y = 0f;
        if (v.magnitude > maxPlayerSpeed) v = v.normalized * maxPlayerSpeed;
        predictedVelocity = v;

        // ancla suavizada: posicion estimada en el instante del ultimo paso
        float tLast = steps[n - 1].time - t0;
        predictionAnchor = pm + v * (tLast - tm);
        predictionAnchor.y = steps[n - 1].pos.y;
        predictionAnchorTime = steps[n - 1].time;

        // error residual del ajuste
        float res = 0f;
        for (int i = 0; i < n; i++)
        {
            float w = steps[i].intensity + 0.05f;
            Vector3 expected = pm + v * ((steps[i].time - t0) - tm);
            Vector3 diff = steps[i].pos - expected;
            diff.y = 0f;
            res += w * diff.sqrMagnitude;
        }
        res = Mathf.Sqrt(res / wSum);
        float fit = Mathf.Sqrt(Mathf.Clamp01(1f - res / Mathf.Max(maxLocalizationError, 0.1f)));

        // regularidad del ritmo: coeficiente de variacion de los intervalos
        float mean = span / (n - 1), var = 0f;
        for (int i = 1; i < n; i++)
        {
            float d = (steps[i].time - steps[i - 1].time) - mean;
            var += d * d;
        }
        float cv = Mathf.Sqrt(var / (n - 1)) / Mathf.Max(mean, 0.01f);

        predictionConfidence = Mathf.Clamp01(1f - cv) * Mathf.Clamp01(v.magnitude / 1.2f) * fit;
    }

    bool HasPrediction(float minConfidence, float maxAge) =>
        steps.Count > 0 && predictionConfidence >= minConfidence && Time.time - predictionAnchorTime < maxAge;

    bool CanPredict => HasPrediction(0.35f, 2f);

    Vector3 PredictedPosition()
    {
        float elapsed = Time.time - predictionAnchorTime + predictionLead;
        Vector3 offset = predictedVelocity.normalized * Mathf.Min(predictedVelocity.magnitude * elapsed, maxPredictDistance);
        return predictionAnchor + offset;
    }

    /// <summary>Promedia los sonidos recientes (ponderados) para reducir el error de localizacion.</summary>
    Vector3 EstimateHeardTarget()
    {
        const float window = 2.5f;
        float wSum = 0f;
        Vector3 acc = Vector3.zero;
        for (int i = memory.Count - 1; i >= 0; i--)
        {
            Heard h = memory[i];
            float age = Time.time - h.time;
            if (age > window) break;
            if (!IsHuntable(h.type)) continue;
            float w = h.intensity * (1f - age / window);
            acc += h.pos * w;
            wSum += w;
        }
        return wSum > 0.001f ? acc / wSum : lastHeardPos;
    }

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

    // Devuelve una lista reutilizada: usarla antes de volver a llamar.
    List<Hotspot> TopHotspots(int count, Vector3 near, float maxDist)
    {
        hotBuffer.Clear();
        foreach (var kv in hotspots)
            if (kv.Value.heat > 0.8f && Vector3.Distance(kv.Value.center, near) <= maxDist) hotBuffer.Add(kv.Value);
        hotBuffer.Sort((x, y) => y.heat.CompareTo(x.heat));
        if (hotBuffer.Count > count) hotBuffer.RemoveRange(count, hotBuffer.Count - count);
        return hotBuffer;
    }

    static Hotspot PickWeighted(List<Hotspot> list)
    {
        float total = 0f;
        foreach (var h in list) total += h.heat;
        float r = Random.value * total;
        foreach (var h in list)
        {
            r -= h.heat;
            if (r <= 0f) return h;
        }
        return list[list.Count - 1];
    }

    /// <summary>
    /// Oye al jugador segun lo que hace: correr se oye lejos, caminar a media distancia
    /// y agachado casi nada. Usa el mismo 'Ruido' que muestra la barra del medidor.
    /// </summary>
    void HearPlayer(float dt)
    {
        if (!hearPlayerDirectly || !playerNoise || playerNoise.Ruido <= 0f) { stepTimer = 0f; return; }

        stepTimer -= dt;
        if (stepTimer > 0f) return;

        bool run = playerNoise.Corriendo;
        stepTimer = run ? 0.3f : (playerNoise.Agachado ? 0.7f : 0.5f);   // cadencia de pasos

        Hear(playerNoise.gameObject, playerNoise.transform.position,
             playerNoise.Ruido * playerHearingRadius, run ? SoundType.Run : SoundType.Footstep);
    }

    /// <summary>Tareas lentas (enfriar zonas, recuperar credibilidad): 1 vez por segundo en vez de cada frame.</summary>
    void SlowTick(float dt)
    {
        slowTimer += dt;
        if (slowTimer < 1f) return;

        DecayHeat(slowTimer);
        for (int i = 0; i < credibility.Length; i++)
            credibility[i] = Mathf.Min(1f, credibility[i] + credibilityRecoveryPerSec * slowTimer);
        slowTimer = 0f;
    }

    // --------------------------------------------------------- COMPORTAMIENTO
    void Update()
    {
        float dt = Time.deltaTime;
        alert = Mathf.Max(0f, alert - alertDecay * dt);
        investigateIntensity = Mathf.Max(0f, investigateIntensity - 0.1f * dt);
        SlowTick(dt);
        HearPlayer(dt);

        // Ataque por contacto (es ciego: solo ataca si lo tiene encima)
        if (state == State.Attack)
        {
            attackTimer -= dt;
            if (player) FaceTowards(player.position, dt);
            if (attackTimer <= 0f)
            {
                agent.isStopped = false;
                if (player) SetExactKnowledge(player.position);
                SetState(State.Hunt);
            }
            return;
        }

        if (player && (player.position - transform.position).sqrMagnitude <= attackRange * attackRange)
        {
            SetState(State.Attack);
            return;
        }
        if (roarTimer > 0f)
        {
            roarTimer -= dt;
            lastHeardTime += dt;              // el rugido no cuenta como "tiempo sin oir nada"
            agent.isStopped = true;
            FaceTowards(lastHeardPos, dt);
            if (roarTimer <= 0f) agent.isStopped = false;
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

        if (TickStuck(dt)) return;

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
        stuckTimer = 0f;
        patrolHotspot = null;
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

    /// <summary>Detecta atascos (puerta cerrada, ruta bloqueada) y replanifica. Devuelve true si intervino.</summary>
    bool TickStuck(float dt)
    {
        bool moving = !agent.isStopped && !agent.pathPending && agent.hasPath && !HasArrived();
        if (!moving || agent.velocity.sqrMagnitude > 0.04f) { stuckTimer = 0f; return false; }

        stuckTimer += dt;
        if (stuckTimer < stuckTime) return false;
        stuckTimer = 0f;

        switch (state)
        {
            case State.Patrol:
            case State.Search:
                hasTarget = false;
                waitTimer = 0f;
                break;
            case State.Investigate:
                BuildSearchQueue(1);
                SetState(State.Search);
                return true;
            case State.Hunt:
                repathTimer = 0f;
                break;
        }
        return false;
    }

    void TickPatrol(float dt)
    {
        if (waitTimer > 0f) { waitTimer -= dt; return; }
        if (!hasTarget)
        {
            hasTarget = SetDest(PickPatrolTarget());
            return;
        }
        if (HasArrived())
        {
            // Llego a una zona caliente y no hay nadie: pierde interes en ella
            if (patrolHotspot != null) { patrolHotspot.heat *= hotspotCooling; patrolHotspot = null; }
            hasTarget = false;
            waitTimer = Random.Range(1f, 2.5f);
        }
    }

    Vector3 PickPatrolTarget()
    {
        patrolHotspot = null;
        // PATRON 2: a veces patrulla donde el jugador hizo mas ruido (eleccion ponderada por calor)
        if (Random.value < patrolHotspotBias)
        {
            var top = TopHotspots(3, transform.position, 60f);
            if (top.Count > 0)
            {
                patrolHotspot = PickWeighted(top);
                return patrolHotspot.center;
            }
        }
        if (patrolPoints != null && patrolPoints.Length > 0)
            return patrolPoints[Random.Range(0, patrolPoints.Length)].position;
        return transform.position + Random.insideUnitSphere * 15f;
    }

    void BeginInvestigate(Vector3 pos, SoundType type, float intensity)
    {
        investigatePos = pos;
        investigateType = type;
        investigateIntensity = intensity;
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
            SetExactKnowledge(player.position);
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
        float fresh = Time.time - lastHeardTime;
        if (fresh > huntMemory)
        {
            BuildSearchQueue(3);
            SetState(State.Search);
            return;
        }

        // Si hace rato que no oye nada, avanza con mas cautela (usa huntFreshSound)
        float k = Mathf.InverseLerp(huntFreshSound, huntMemory, fresh);
        agent.speed = Mathf.Lerp(huntSpeed, investigateSpeed, k);

        // Llego a donde creia que estaba el jugador y no oye nada nuevo: busca alrededor
        if (hasTarget && HasArrived() && fresh > huntFreshSound)
        {
            BuildSearchQueue(3);
            SetState(State.Search);
            return;
        }

        repathTimer -= dt;
        if (repathTimer > 0f) return;
        repathTimer = 0.25f;
        hasTarget = SetDest(CanPredict ? PredictedPosition() : EstimateHeardTarget());
    }

    void BuildSearchQueue(int hotspotCount)
    {
        searchQueue.Clear();
        candidateBuffer.Clear();

        Vector3 cur = transform.position;
        if (HasPrediction(0.2f, 8f))
        {
            cur = PredictedPosition();
            searchQueue.Enqueue(cur);   // lo mas probable, siempre primero
        }

        foreach (var h in TopHotspots(hotspotCount, lastHeardPos, 30f)) candidateBuffer.Add(h.center);

        for (int i = 0; i < 3; i++)
        {
            float ang = Random.value * Mathf.PI * 2f;
            float r = Random.Range(0.3f, 1f) * searchRadius;
            Vector3 p = lastHeardPos + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
            if (NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas)) p = hit.position;
            candidateBuffer.Add(p);
        }

        // Orden por vecino mas cercano para no hacer zigzag
        while (candidateBuffer.Count > 0)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < candidateBuffer.Count; i++)
            {
                float d = (candidateBuffer[i] - cur).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            cur = candidateBuffer[best];
            searchQueue.Enqueue(cur);
            candidateBuffer.RemoveAt(best);
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

    bool HasArrived()
    {
        if (agent.pathPending) return false;
        if (agent.pathStatus == NavMeshPathStatus.PathInvalid) return true;
        return agent.remainingDistance <= agent.stoppingDistance + 0.3f;
    }

    void FaceTowards(Vector3 worldPos, float dt)
    {
        Vector3 d = worldPos - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 6f * dt);
    }

    void TickCreep(float dt)
    {
        creepTimer -= dt;
        if (creepTimer > 0f) return;
        creepPaused = !creepPaused;
        creepTimer = Random.Range(creepPaused ? pauseTime.x : moveBurst.x,
                                  creepPaused ? pauseTime.y : moveBurst.y);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        foreach (var h in memory) Gizmos.DrawWireSphere(h.pos, 0.3f + h.intensity * 0.5f);

        Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.6f);
        foreach (var kv in hotspots) Gizmos.DrawWireSphere(kv.Value.center, Mathf.Clamp(kv.Value.heat * 0.4f, 0.5f, 4f));

        if (Application.isPlaying && HasPrediction(0.2f, 8f))
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(PredictedPosition(), 0.4f);
        }
    }
    public void Roar(float duration)
    {
        roarTimer = duration;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }
}