using UnityEngine;
using CarRace.Vehicle;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The player's car setups, one per circuit, and their driver assists, kept between sessions
    /// in PlayerPrefs. A setup is CarSetup's text, the changed settings only, under the circuit's
    /// scene name, so a circuit nobody has tuned for has no entry and drives the car as built.
    /// The assists are shared by every circuit: they are about the driver, not the track.
    ///
    /// The lobby's setup screen writes here; CarController reads it as the player's car wakes.
    /// </summary>
    public static class SetupStore
    {
        const string SetupKey = "CarRace.Setup.";
        const string AssistKey = "CarRace.Assist.";
        const string BodyKey = "CarRace.CarBody";

        /// <summary>The player's body by name (CarDesigns), whose steering the setups are changes
        /// to; kept by the lobby whenever the body is chosen.</summary>
        public static string Body
        {
            get => PlayerPrefs.GetString(BodyKey, "");
            set { PlayerPrefs.SetString(BodyKey, value ?? ""); PlayerPrefs.Save(); }
        }

        /// <summary>The car as built with the player's body's steering (BodySteering): what a
        /// setup is a change to, and what DEFAULT goes back to.</summary>
        public static CarConfig Baseline(CarConfig built)
        {
            CarConfig car = built.Clone();
            BodySteering.Apply(car, Body);
            return car;
        }

        public enum Assist { Abs, TractionControl, EngineBrakingControl, AutomaticGearbox }

        /// <summary>The setup saved for a circuit, against the car it is a change to.</summary>
        public static CarSetup Load(string scene, CarConfig baseline)
            => CarSetup.FromText(baseline, PlayerPrefs.GetString(SetupKey + scene, ""));

        public static void Save(string scene, CarSetup setup)
        {
            string text = setup.ToText();
            if (text.Length == 0) PlayerPrefs.DeleteKey(SetupKey + scene);
            else PlayerPrefs.SetString(SetupKey + scene, text);
            PlayerPrefs.Save();
        }

        /// <summary>Every assist starts on, as the car always had them.</summary>
        public static bool Get(Assist assist) => PlayerPrefs.GetInt(AssistKey + assist, 1) != 0;

        public static void Set(Assist assist, bool on)
        {
            PlayerPrefs.SetInt(AssistKey + assist, on ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
