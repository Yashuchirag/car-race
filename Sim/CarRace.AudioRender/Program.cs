using System;
using System.IO;
using CarRace.UnityGame.Audio;

// A scripted drive through the synthesised sound, written as 48 kHz mono WAVs: the engine
// alone, the tyres and road alone, and the two mixed as the player's car hears them. The
// drive: idle, a blip, a launch up through five gears with the car's real ratios, the
// limiter in second, a lift and downshifts, then a slide, kerbs and grass. The control
// track (rpm, throttle, gear, speed) is written beside it as CSV.

const int Rate = 48000;
string outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);

float[] ratios = { 3.60f, 2.30f, 1.70f, 1.32f, 1.08f, 0.88f };
const float FinalDrive = 3.44f, WheelRadius = 0.34f, Idle = 900f, Limit = 7600f;
float SpeedAt(float rpm, int gear) => rpm / 60f / ratios[gear - 1] / FinalDrive * 2f * MathF.PI * WheelRadius;
float RpmAt(float speed, int gear) => MathF.Max(Idle, speed / (2f * MathF.PI * WheelRadius) * 60f * ratios[gear - 1] * FinalDrive);

var engine = new EngineSynth(Rate) { LimitRpm = Limit };
var tyres = new TyreSynth(Rate) { Wind = 1f };
float seconds = 34f;
int n = (int)(seconds * Rate);
var engineOut = new float[n];
var tyresOut = new float[n];
var mix = new float[n];
using var csv = new StreamWriter(Path.Combine(outDir, "drive.csv"));
csv.WriteLine("t,rpm,throttle,gear,speed_kph,squeal,kerb,offroad");

float rpm = Idle, throttle = 0f, speed = 0f, shiftUntil = -1f;
int gear = 1;
for (int i = 0; i < n; i++)
{
    float t = i / (float)Rate, dt = 1f / Rate;
    float squeal = 0f, kerb = 0f, offRoad = 0f;
    if (t < 2f) { throttle = 0f; rpm = Idle; }
    else if (t < 3.5f)                            // a blip in neutral
    {
        throttle = t < 2.35f ? 1f : 0f;
        rpm += (throttle > 0 ? 9000f : -3500f) * dt;
        rpm = Math.Clamp(rpm, Idle, 6000f);
    }
    else if (t < 17f)                             // launch and up through the gears
    {
        bool holdLimiter = gear == 2 && t > 7.2f && t < 8.7f;
        throttle = t < shiftUntil ? 0f : 1f;
        float accel = 11f / gear;                  // m/s2, falling with each gear
        if (t >= shiftUntil) speed += accel * dt * (rpm > 6800f ? 0.85f : 1f);
        if (speed < 2f) rpm = MathF.Max(rpm, 3600f); // launch on the clutch
        else rpm = RpmAt(speed, gear);
        if (holdLimiter) { rpm = Limit; speed = SpeedAt(Limit, gear); }
        else if (rpm > 7450f && gear < 5) { gear++; shiftUntil = t + 0.08f; rpm = RpmAt(speed, gear); }
    }
    else if (t < 24f)                             // lift, brake, downshift to third
    {
        throttle = 0f;
        speed = MathF.Max(20f, speed - 9f * dt);
        if (gear > 3 && RpmAt(speed, gear - 1) < 6800f) gear--;
        rpm = RpmAt(speed, gear);
    }
    else                                          // back on it through a corner: slide, kerb, grass
    {
        throttle = t < 27f ? 0.8f : 0.4f;
        speed = MathF.Min(speed + 2f * dt, 45f);
        rpm = RpmAt(speed, gear);
        squeal = t > 24.5f && t < 27f ? MathF.Sin((t - 24.5f) / 2.5f * MathF.PI) : 0f;
        kerb = t > 27.5f && t < 29f ? 0.5f : 0f;
        offRoad = t > 30f && t < 32.5f ? 0.75f : 0f;
    }

    engine.Rpm = rpm;
    engine.Throttle = throttle;
    tyres.SpeedMs = speed;
    tyres.Squeal = squeal;
    tyres.Kerb = kerb;
    tyres.OffRoad = offRoad;
    engineOut[i] = engine.Next();
    tyresOut[i] = tyres.Next();
    mix[i] = MathF.Tanh(0.7f * engineOut[i] + 0.8f * tyresOut[i]);
    if (i % 480 == 0)
        csv.WriteLine($"{t:0.00},{rpm:0},{throttle:0.00},{gear},{speed * 3.6f:0.0},{squeal:0.00},{kerb:0.00},{offRoad:0.00}");
}

Write(Path.Combine(outDir, "engine.wav"), engineOut);
Write(Path.Combine(outDir, "tyres.wav"), tyresOut);
Write(Path.Combine(outDir, "drive.wav"), mix);
Console.WriteLine($"Rendered {seconds} s to {outDir}: engine.wav, tyres.wav, drive.wav, drive.csv");

static void Write(string path, float[] samples)
{
    using var w = new BinaryWriter(File.Create(path));
    int bytes = samples.Length * 2;
    w.Write("RIFF"u8.ToArray()); w.Write(36 + bytes); w.Write("WAVE"u8.ToArray());
    w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
    w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
    w.Write("data"u8.ToArray()); w.Write(bytes);
    foreach (float s in samples) w.Write((short)Math.Clamp(s * 32767f, -32768f, 32767f));
}
