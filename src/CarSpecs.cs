using System;
using System.Collections.Generic;

namespace OTH.Fuel;

// Tank capacity per car, in litres of game fuel: the real car's stock tank
// divided by Mod.TankRatio, since the map is far smaller than real distances.
//
// Keys must equal the live car's CarInfo asset name (case-insensitive); the
// asset names are logged once per car spawn ("CarInfo '...'"). Burro,
// DingDing, Protector and Trailrat are confirmed from logs; the others follow
// CarAudioManager.VehicleType and need confirming. Real models are from the
// OTH.RealVehicleNames mod; tank sizes are stock figures from memory
// (approximate, the base model where a car had options).
internal static class CarSpecs
{
    // A car missing from the table (or before its CarInfo is known).
    private const float DefaultRealLitres = 60f;

    internal static float DefaultCapacity => DefaultRealLitres / Mod.TankRatio;

    private static readonly Dictionary<string, float> realLitres = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mammoth"] = 90f,      // Mercedes-Benz Unimog 406
        ["Marshal"] = 109f,     // Land Rover 101 FC
        ["Lynx"] = 83f,         // Volkswagen Iltis
        ["Wombat"] = 83f,       // Mercedes-Benz W460 SWB
        ["Brick"] = 90f,        // Toyota Land Cruiser 70 SWB
        ["Bull"] = 48f,         // Ford Bronco Mk1
        ["DingDing"] = 63f,     // Toyota Land Cruiser FJ40
        ["Polyana"] = 42f,      // Lada Niva
        ["Protector"] = 55f,    // Land Rover Defender 90
        ["Tinny"] = 45f,        // Land Rover Series I SWB
        ["Trailrat"] = 42f,     // International Scout 80
        ["Cumbre"] = 76f,       // Chevrolet K10
        ["GrandWoodman"] = 77f, // Jeep Grand Wagoneer
        ["Roamer"] = 73f,       // Ford Explorer Mk1
        ["TrailBurner"] = 95f,  // Chevrolet K5 Blazer
        ["Woodman"] = 76f,      // Jeep Wagoneer
        ["RustOx"] = 70f,       // Mercedes-Benz T2 camper
        ["BlastWagon"] = 68f,   // Dodge Power Wagon
        ["Boar"] = 60f,         // Volvo TP21 Sugga
        ["Burro"] = 70f,        // Toyota Land Cruiser FJ45
        ["Timberbox"] = 68f,    // Dodge Power Wagon woodie
        ["Panda"] = 35f,        // Fiat Panda 4x4 (Grizzly; asset name unconfirmed,
        ["Grizzly"] = 35f,      // the game files have a Car_Grizzly prefab)
        ["Rancho"] = 56f,       // Matra-Simca Rancho
    };

    // A jerry can holds half a Burro tank (Toyota FJ45, 70 L real), the same
    // for every car, scaled by the same ratio as the tanks.
    internal static float JerryCanLitres => realLitres["Burro"] * 0.5f / Mod.TankRatio;

    // Returns the capacity and whether the car is in the table (exact name match).
    internal static (float capacity, bool listed) Capacity(string carInfoName)
    {
        if (!string.IsNullOrEmpty(carInfoName) && realLitres.TryGetValue(carInfoName, out float real))
        {
            return (real / Mod.TankRatio, true);
        }
        return (DefaultCapacity, false);
    }
}
