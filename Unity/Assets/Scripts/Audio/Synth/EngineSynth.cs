using System;

namespace CarRace.UnityGame.Audio
{
    /// <summary>
    /// A cross-plane V8, synthesised from what the engine is doing rather than played from
    /// recordings, so it follows the revs, the throttle and every shift exactly and never
    /// repeats. The model is the one racing sims use in some form: each cylinder's firing
    /// sends a pressure pulse into the exhaust, and what is heard is those pulses coloured by
    /// the pipes and the silencer.
    ///
    ///   Firing: eight cylinders every 90 degrees of crank over the 720 of a four-stroke
    ///   cycle, in the cross-plane order, which sends them to the two banks unevenly (left,
    ///   right, left, left, right, left, right, right). Each bank hears an uneven beat, and
    ///   that is the V8 burble. Every cylinder is a few per cent stronger or weaker than the
    ///   next, and every firing varies a little, as real combustion does.
    ///   Pulse: an impulse through a low pass that opens with the revs, so the pulses get
    ///   sharper as they come faster. Strength follows the load: full throttle is loud and
    ///   hard, a closed throttle a soft, uneven chuff with the odd pop on the overrun.
    ///   Exhaust: per bank a header (0.7 m, open end, a negative echo) and a main pipe
    ///   (2 m), as comb filters, then a silencer: a low pass that opens under load, and a
    ///   resonance near 120 Hz for the body of the sound.
    ///   Intake and engine: a band of turbulent noise that grows with load and revs, and a
    ///   little high mechanical noise that grows with revs.
    ///   Limiter: at the rev limit under throttle the ignition is cut in bursts, the
    ///   stutter every racing engine makes there.
    ///
    /// Set Rpm and Throttle from the game; call Next once per output sample.
    /// </summary>
    public sealed class EngineSynth
    {
        public float Rpm = 900f, Throttle;
        public float LimitRpm = 7600f;
        /// <summary>Rate multiplier, for the Doppler shift of a passing car.</summary>
        public float Pitch = 1f;
        public float Volume = 1f;
        const float Drive = 0.55f;

        static readonly int[] BankOf = { 0, 1, 0, 0, 1, 0, 1, 1 };

        readonly float _sampleRate;
        readonly float[] _cylinderGain = new float[8];
        Noise _noise;
        Smoothed _rpm, _load;
        double _crank, _nextFiring;   // degrees, running on
        int _next;                     // the next cylinder in firing order
        float _limiterClock;

        Biquad _pulseL, _pulseR, _silencerL, _silencerR, _bodyL, _bodyR, _intake, _intakePulse, _mechanical;
        readonly Pipe _headerL, _headerR, _mainL, _mainR;
        DcBlocker _dc;

        public EngineSynth(float sampleRate, uint seed = 1)
        {
            _sampleRate = sampleRate;
            _noise = new Noise(seed);
            for (int c = 0; c < 8; c++) _cylinderGain[c] = 0.92f + 0.16f * _noise.Unit();
            _rpm = new Smoothed(Rpm, 0.03f, sampleRate);
            _load = new Smoothed(0f, 0.05f, sampleRate);
            _pulseL = Biquad.Make(Biquad.Kind.LowPass, 700f, 0.7f, sampleRate);
            _pulseR = Biquad.Make(Biquad.Kind.LowPass, 700f, 0.7f, sampleRate);
            _silencerL = Biquad.Make(Biquad.Kind.LowPass, 1500f, 0.8f, sampleRate);
            _silencerR = Biquad.Make(Biquad.Kind.LowPass, 1500f, 0.8f, sampleRate);
            _bodyL = Biquad.Make(Biquad.Kind.BandPass, 118f, 2f, sampleRate);
            _bodyR = Biquad.Make(Biquad.Kind.BandPass, 126f, 2f, sampleRate);
            _intake = Biquad.Make(Biquad.Kind.BandPass, 1800f, 0.8f, sampleRate);
            _intakePulse = Biquad.Make(Biquad.Kind.BandPass, 480f, 1.5f, sampleRate);
            _mechanical = Biquad.Make(Biquad.Kind.HighPass, 4200f, 0.7f, sampleRate);
            _headerL = new Pipe(0.0029f, -0.4f, sampleRate);
            _headerR = new Pipe(0.0033f, -0.4f, sampleRate);
            _mainL = new Pipe(0.0078f, 0.28f, sampleRate);
            _mainR = new Pipe(0.0084f, 0.28f, sampleRate);
        }

        public float Next()
        {
            float rpm = _rpm.Toward(Rpm);
            float load = _load.Toward(Math.Clamp(Throttle, 0f, 1f));
            float dt = 1f / _sampleRate;

            // Crank: the cylinders fire every 90 degrees; which bank each one feeds is what
            // makes the beat uneven.
            _crank += rpm / 60.0 * 360.0 * dt * Pitch;
            float left = 0f, right = 0f;
            bool limiting = rpm > LimitRpm - 80f && Throttle > 0.5f;
            _limiterClock = limiting ? _limiterClock + dt : 0f;
            bool cut = limiting && (_limiterClock % 0.07f) < 0.035f;
            while (_crank >= _nextFiring)
            {
                float strength = Strength(load, rpm, cut) * _cylinderGain[_next];
                if (BankOf[_next] == 0) left += strength; else right += strength;
                _next = (_next + 1) % 8;
                _nextFiring += 90.0;
            }
            if (_crank > 7200.0) { _crank -= 7200.0; _nextFiring -= 7200.0; }

            // Pulses, sharper as the revs rise; the exhaust; the silencer, more open under load.
            float sharp = 450f + 0.16f * rpm;
            _pulseL.Tune(sharp);
            _pulseR.Tune(sharp);
            float open = 1100f + 2200f * load + 0.12f * rpm;
            _silencerL.Tune(open);
            _silencerR.Tune(open);
            float pl = _pulseL.Process(left * 40f), pr = _pulseR.Process(right * 40f);
            float el = _mainL.Process(_headerL.Process(pl)), er = _mainR.Process(_headerR.Process(pr));
            float exhaust = _silencerL.Process(el) + _silencerR.Process(er) + 0.6f * (_bodyL.Process(el) + _bodyR.Process(er));

            float revs = rpm / LimitRpm;
            float intake = _intake.Process(_noise.Next()) * (0.015f + 0.2f * load) * revs * MathF.Sqrt(revs)
                         + _intakePulse.Process(pl + pr) * 0.25f * load;
            float mechanical = _mechanical.Process(_noise.Next()) * 0.02f * revs * revs;

            // A gentle saturation for grit, then the DC it leaves taken out.
            return _dc.Process(MathF.Tanh((exhaust + intake + mechanical) * Drive)) * Volume;
        }

        /// <summary>One firing: stronger with load, a few per cent different every time;
        /// under a closed throttle soft and uneven, with a pop now and then above 3,000 rpm.</summary>
        float Strength(float load, float rpm, bool cut)
        {
            if (cut) return 0.03f;
            float s = (0.16f + 0.84f * load) * (0.9f + 0.2f * _noise.Unit());
            if (load < 0.1f && rpm > 3000f)
            {
                float roll = _noise.Unit();
                if (roll < 0.06f) s *= 0.25f;
                else if (roll > 0.995f) s = 1.1f;
            }
            return s;
        }
    }
}
