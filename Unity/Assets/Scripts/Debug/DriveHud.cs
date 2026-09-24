using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// A plain on-screen readout for the feel test: speed, gear, rpm, and exactly what the
    /// car is being told to do, including the raw input axes behind it.
    ///
    /// The raw axes are the point. A car that moves with nobody touching anything is either
    /// settling on its suspension, which shows as speed with all inputs at zero, or being fed
    /// input from somewhere unexpected, such as a joystick axis that rests off zero, which
    /// shows as a non-zero axis here. Guessing between the two from the car's motion alone
    /// is not possible. Diagnostic only, and kept outside Scripts/Game on purpose.
    /// </summary>
    public sealed class DriveHud : MonoBehaviour
    {
        [SerializeField] CarController car;
        [SerializeField] DriverInput driver;
        [SerializeField] bool showRawAxes = true;

        GUIStyle _style;

        void OnGUI()
        {
            // Development readout: the editor and development builds only. Players have the
            // dashboard dials for speed, gear and revs.
            if (!Debug.isDebugBuild || Hud.Hidden || car == null || car.Sim == null) return;
            _style ??= new GUIStyle(GUI.skin.label) { normal = { textColor = Color.white } };
            _style.fontSize = Hud.Font(18);

            var drivetrain = car.Sim.Drivetrain;
            string gear = drivetrain.Gear switch { -1 => "R", 0 => "N", _ => drivetrain.Gear.ToString() };
            string text = $"{car.SpeedKph,5:0} km/h   gear {gear}   {drivetrain.EngineRpm,5:0} rpm";

            if (driver != null)
            {
                var input = driver.Read();
                text += $"\nsteer {input.Steer,5:+0.00;-0.00}   throttle {input.Throttle:0.00}   " +
                        $"brake {input.Brake:0.00}   handbrake {input.Handbrake:0}";
            }

            if (showRawAxes)
            {
                text += $"\nraw: Horizontal {Axis("Horizontal")}  Vertical {Axis("Vertical")}  " +
                        $"Throttle {Axis("Throttle")}  Brake {Axis("Brake")}";
            }

            GUI.Box(new Rect(Hud.Px(10), Hud.Px(10), Hud.Px(700), Hud.Px(showRawAxes ? 92 : 68)), GUIContent.none);
            GUI.Label(new Rect(Hud.Px(20), Hud.Px(14), Hud.Px(690), Hud.Px(90)), text, _style);
        }

        static string Axis(string name)
        {
            try { return Input.GetAxisRaw(name).ToString("+0.00;-0.00"); }
            catch (System.ArgumentException) { return "n/a"; }
        }
    }
}
