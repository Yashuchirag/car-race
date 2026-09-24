using System.Globalization;
using System.IO;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Writes the car's state to a CSV every physics step while in Play mode, the Unity
    /// counterpart of the harness's --csv. Files go to Telemetry/ in the project folder,
    /// beside Assets and outside it, one per run, named by start time.
    ///
    /// For questions a person driving cannot answer by feel: whether the front tyres are
    /// past their peak slip angle when a corner will not turn in, whether ABS is releasing
    /// the brakes, what the keyboard is actually asking for. Every hard physics question in
    /// this project was settled by reading data like this rather than by reasoning.
    /// Diagnostic only, and kept outside Scripts/Game on purpose.
    /// </summary>
    public sealed class TelemetryRecorder : MonoBehaviour
    {
        [SerializeField] CarController car;
        [SerializeField] DriverInput driver;
        [SerializeField] bool record = true;

        StreamWriter _csv;
        Rigidbody _body;
        Vector3 _lastVelocity;
        float _time;

        void Start()
        {
            // Editor and development builds only. In a release build it stalled the game for
            // 90 ms half a minute into a race, measured by the benchmark with and without it,
            // and a player has no use for the file.
            if (!record || !Debug.isDebugBuild || car == null || car.Sim == null) { enabled = false; return; }
            _body = car.GetComponent<Rigidbody>();

            string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Telemetry");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"run-{System.DateTime.Now:yyyyMMdd-HHmmss}.csv");
            _csv = new StreamWriter(path);
            _csv.WriteLine("t,x,y,z,speed_kph,fwd_kph,steer_in,throttle,brake,handbrake,steer_rack,steer_fl_deg,steer_fr_deg," +
                           "sideslip_deg,yaw_rate,lat_g,long_g,gear,rpm," +
                           "slip_fl,slip_fr,slip_rl,slip_rr,ratio_fl,ratio_fr,ratio_rl,ratio_rr," +
                           "load_fl,load_fr,load_rl,load_rr,brake_scale_fl,brake_scale_rl,flat_lat_n_f,flat_lat_n_r");
            Debug.Log($"Telemetry recording to {path}");
        }

        void FixedUpdate()
        {
            if (_csv == null) return;
            float dt = Time.fixedDeltaTime;
            _time += dt;

            var inputs = driver != null ? driver.Read() : default;
            var sim = car.Sim;
            var w = sim.Wheels;
            Vector3 v = _body.linearVelocity;
            Vector3 accel = (v - _lastVelocity) / dt;
            _lastVelocity = v;

            Transform t = car.transform;
            float forward = Vector3.Dot(v, t.forward);
            float right = Vector3.Dot(v, t.right);
            float sideslip = v.magnitude > 0.5f ? Mathf.Atan2(right, forward) * Mathf.Rad2Deg : 0f;
            float latG = Vector3.Dot(accel, t.right) / 9.81f;
            float longG = Vector3.Dot(accel, t.forward) / 9.81f;

            var c = CultureInfo.InvariantCulture;
            string F(float x, string f = "0.###") => x.ToString(f, c);
            _csv.WriteLine(string.Join(",", new[]
            {
                F(_time, "0.000"), F(t.position.x, "0.00"), F(t.position.y, "0.00"), F(t.position.z, "0.00"),
                F(v.magnitude * 3.6f, "0.00"), F(forward * 3.6f, "0.00"),
                F(inputs.Steer), F(inputs.Throttle), F(inputs.Brake), F(inputs.Handbrake),
                F(sim.SteerPosition), F(w[0].SteerAngle * Mathf.Rad2Deg, "0.00"), F(w[1].SteerAngle * Mathf.Rad2Deg, "0.00"),
                F(sideslip, "0.00"), F(Vector3.Dot(_body.angularVelocity, t.up), "0.0000"), F(latG), F(longG),
                sim.Drivetrain.Gear.ToString(c), F(sim.Drivetrain.EngineRpm, "0"),
                F(w[0].SlipAngle * Mathf.Rad2Deg, "0.00"), F(w[1].SlipAngle * Mathf.Rad2Deg, "0.00"),
                F(w[2].SlipAngle * Mathf.Rad2Deg, "0.00"), F(w[3].SlipAngle * Mathf.Rad2Deg, "0.00"),
                F(w[0].SlipRatio), F(w[1].SlipRatio), F(w[2].SlipRatio), F(w[3].SlipRatio),
                F(w[0].Load, "0"), F(w[1].Load, "0"), F(w[2].Load, "0"), F(w[3].Load, "0"),
                F(w[0].BrakeScale), F(w[2].BrakeScale),
                F(w[0].ForceLat + w[1].ForceLat, "0"), F(w[2].ForceLat + w[3].ForceLat, "0"),
            }));
        }

        void OnDestroy()
        {
            _csv?.Dispose();
            _csv = null;
        }
    }
}
