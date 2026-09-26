using CarRace.UnityGame.Audio;
using CarRace.Vehicle;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// A car's sound: its engine and tyres synthesised (EngineSynth, TyreSynth) from the
    /// vehicle model's own state, and its knocks from recordings. The player's car is heard
    /// flat, as a chase camera hears it, with wind; every other car from where it is, with
    /// its engine Doppler-shifted as it closes or pulls away.
    ///
    ///   Engine: the model's engine rpm, and the throttle it was given, cut to nothing while
    ///   a shift is in progress, which is the dip in every shift.
    ///   Squeal: each grounded wheel on asphalt past 92% of its grip, as the tyre model
    ///   measures it (Wheel.GripUsage).
    ///   Off road: wheels on a surface with less grip than asphalt.
    ///   Kerbs: wheels whose contact point is over a kerb, found by a short ray down from
    ///   each contact 20 times a second.
    ///   Knocks: a hit on anything but the ground (a steep contact normal), at a volume set
    ///   by the impulse; a hard one is a body thud with a panel crunch over it, a light one a
    ///   tap. Sliding along something is a scrape for as long as it lasts.
    ///
    /// Added to every car by RaceDirector.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public sealed class CarAudio : MonoBehaviour
    {
        const float SquealFromUsage = 0.92f, SquealRange = 0.45f;
        const float OffRoadBelowFriction = 0.8f;
        const float HitFromImpulse = 900f, HitFullImpulse = 16000f;
        const float SpeedOfSound = 343f;

        bool _player;
        CarController _car;
        Rigidbody _body;
        SynthVoice _voice;
        AudioSource _knocks;
        float _kerbClock, _kerb, _knockCooldown, _scrapeMs, _scrapeSeen, _lastDistance = -1f;

        static AudioClip[] _thuds, _panels, _taps;

        public static void Attach(CarController car, bool player)
        {
            var audio = car.gameObject.AddComponent<CarAudio>();
            audio._player = player;
        }

        void Start()
        {
            _car = GetComponent<CarController>();
            _body = GetComponent<Rigidbody>();
            int rate = AudioSettings.outputSampleRate;
            uint seed = (uint)(GetInstanceID() & 0x7fffffff) | 1u;

            var voice = new GameObject("Car Audio");
            voice.transform.SetParent(transform, false);
            AudioSource source = voice.AddComponent<AudioSource>();
            Place(source);
            source.clip = SynthVoice.Ones();
            source.loop = true;
            source.volume = _player ? 0.85f : 1f;
            _voice = voice.AddComponent<SynthVoice>();
            _voice.Engine = new EngineSynth(rate, seed) { LimitRpm = _car.Sim?.Config.RevLimitRpm ?? 7600f };
            _voice.Tyres = new TyreSynth(rate, seed * 7u) { Wind = _player ? 1f : 0f };
            _voice.EngineLevel = 0.6f;
            _voice.TyreLevel = _player ? 0.7f : 0.55f;
            source.Play();

            _knocks = gameObject.AddComponent<AudioSource>();
            Place(_knocks);
            _knocks.playOnAwake = false;
            _thuds ??= Load("thud");
            _panels ??= Load("panel");
            _taps ??= Load("tap");
        }

        void Place(AudioSource source)
        {
            source.spatialBlend = _player ? 0f : 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 10f;
            source.maxDistance = 600f;
            source.dopplerLevel = 0f;          // the engine does its own; the clip is constant
            source.priority = _player ? 0 : 64;
        }

        static AudioClip[] Load(string name)
        {
            var clips = new AudioClip[5];
            for (int i = 0; i < clips.Length; i++) clips[i] = Resources.Load<AudioClip>($"Audio/Impacts/{name}_{i}");
            return clips;
        }

        void Update()
        {
            VehicleSim sim = _car.Sim;
            if (sim == null || _voice == null) return;
            float dt = Time.deltaTime;

            EngineSynth engine = _voice.Engine;
            engine.Rpm = sim.Drivetrain.EngineRpm;
            engine.Throttle = sim.Drivetrain.ShiftInProgress ? 0f : _car.LastInputs.Throttle;
            if (!_player) engine.Pitch = Doppler(dt);

            float squeal = 0f;
            int offRoad = 0;
            for (int w = 0; w < 4; w++)
            {
                Wheel wheel = sim.Wheels[w];
                if (!wheel.Grounded) continue;
                if (wheel.SurfaceFriction < OffRoadBelowFriction) { offRoad++; continue; }
                squeal += Mathf.Clamp01((wheel.GripUsage - SquealFromUsage) / SquealRange);
            }
            _kerbClock -= dt;
            if (_kerbClock <= 0f)
            {
                _kerbClock = 0.05f;
                _kerb = OnKerbs(sim) / 4f;
            }
            _scrapeSeen -= dt;
            if (_scrapeSeen <= 0f) _scrapeMs = 0f;
            _knockCooldown -= dt;

            TyreSynth tyres = _voice.Tyres;
            tyres.SpeedMs = _body.linearVelocity.magnitude;
            tyres.Squeal = Mathf.Clamp01(squeal * 0.6f);
            tyres.OffRoad = offRoad / 4f;
            tyres.Kerb = _kerb;
            tyres.ScrapeMs = _scrapeMs;
        }

        /// <summary>How the engine's pitch shifts as the car closes on the listener or pulls
        /// away: c / (c - closing speed), eased, and at 60% of the physical shift, which reads
        /// as right in a game where the real one sounds overdone.</summary>
        float Doppler(float dt)
        {
            AudioListener listener = FindListener();
            if (listener == null || dt <= 0f) return 1f;
            float distance = Vector3.Distance(listener.transform.position, transform.position);
            float closing = _lastDistance < 0f ? 0f : (_lastDistance - distance) / dt;
            _lastDistance = distance;
            float shift = SpeedOfSound / (SpeedOfSound - Mathf.Clamp(closing, -80f, 80f) * 0.6f);
            return Mathf.Lerp(_voice.Engine.Pitch, shift, Mathf.Min(1f, dt * 8f));
        }

        static AudioListener _listener;
        static AudioListener FindListener()
        {
            if (_listener == null) _listener = FindFirstObjectByType<AudioListener>();
            return _listener;
        }

        int OnKerbs(VehicleSim sim)
        {
            int count = 0;
            for (int w = 0; w < 4; w++)
            {
                Wheel wheel = sim.Wheels[w];
                if (!wheel.Grounded) continue;
                Vector3 contact = Bridge.ToUnity(wheel.ContactPoint);
                if (UnityEngine.Physics.Raycast(contact + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 0.6f)
                    && hit.collider.name.StartsWith("Kerbs")) count++;
            }
            return count;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (Ground(collision) || _knockCooldown > 0f || _knocks == null) return;
            float impulse = collision.impulse.magnitude;
            if (impulse < HitFromImpulse) return;
            _knockCooldown = 0.15f;
            float strength = Mathf.Clamp01(impulse / HitFullImpulse);
            _knocks.pitch = Random.Range(0.92f, 1.08f);
            if (strength < 0.3f)
                _knocks.PlayOneShot(Pick(_taps), 0.35f + strength);
            else
            {
                _knocks.PlayOneShot(Pick(_thuds), 0.5f + 0.5f * strength);
                _knocks.PlayOneShot(Pick(_panels), 0.3f + 0.6f * strength);
            }
        }

        void OnCollisionStay(Collision collision)
        {
            if (Ground(collision)) return;
            Vector3 relative = collision.relativeVelocity;
            Vector3 normal = collision.GetContact(0).normal;
            _scrapeMs = Mathf.Max(_scrapeMs, (relative - Vector3.Dot(relative, normal) * normal).magnitude);
            _scrapeSeen = 0.1f;
        }

        /// <summary>The body touching the road over a crest, not a hit.</summary>
        static bool Ground(Collision collision) =>
            collision.contactCount == 0 || Mathf.Abs(collision.GetContact(0).normal.y) > 0.7f;

        static AudioClip Pick(AudioClip[] clips) => clips[Random.Range(0, clips.Length)];
    }
}
