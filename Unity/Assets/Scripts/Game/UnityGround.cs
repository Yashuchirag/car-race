using UnityEngine;
using CarRace.Vehicle;
using Vec3 = System.Numerics.Vector3;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Answers the model's suspension probes with a sphere cast against the scene.
    ///
    /// A sphere rather than a ray because a ray drops into every crack between road
    /// mesh triangles and reads it as air, which shows up as a wheel chattering on a
    /// smooth straight. The sphere is the tyre's own radius, so a kerb edge lifts the
    /// wheel the way the tyre's shoulder would.
    /// </summary>
    public sealed class UnityGround : IGround
    {
        readonly int _layerMask;
        readonly Rigidbody _self;
        bool _selfHitReported;

        /// <param name="layerMask">Surfaces the wheels can stand on. Must NOT include the
        /// layer the car's own colliders are on: the probe starts at the top of the strut,
        /// which is inside the body, so a mask that includes the car reports full
        /// compression on every wheel and throws it into the air.</param>
        /// <param name="self">The car's rigidbody, used only to notice that mistake.</param>
        public UnityGround(int layerMask, Rigidbody self)
        {
            _layerMask = layerMask;
            _self = self;
        }

        public GroundHit Probe(Vec3 origin, Vec3 direction, float maxDistance, float radius)
        {
            Vector3 from = Bridge.ToUnity(origin);
            Vector3 along = Bridge.ToUnity(direction);

            // maxDistance is how far down the model looks for the road surface, but a
            // sphere cast measures how far the sphere's centre travels, which is one
            // radius less. Returning the travel distance unchanged would report the
            // car riding a radius higher than it is, on stilts.
            float travel = maxDistance - radius;

            if (travel > 0f &&
                UnityEngine.Physics.SphereCast(from, radius, along, out RaycastHit sphere, travel,
                                               _layerMask, QueryTriggerInteraction.Ignore))
            {
                if (IsSelf(sphere)) return Miss();
                return new GroundHit
                {
                    Hit = true,
                    Distance = sphere.distance + radius,
                    Normal = Bridge.ToSim(sphere.normal),
                    Friction = FrictionOf(sphere.collider),
                };
            }

            // A sphere cast that starts already overlapping a collider reports nothing.
            // Without this fallback a wheel buried in a kerb goes quietly airborne, so
            // the car keeps the grip of a wheel that is visibly in the ground. A ray is
            // enough here because the wheel is touching by definition.
            if (UnityEngine.Physics.Raycast(from, along, out RaycastHit ray, maxDistance,
                                            _layerMask, QueryTriggerInteraction.Ignore))
            {
                if (IsSelf(ray)) return Miss();
                return new GroundHit
                {
                    Hit = true,
                    Distance = ray.distance,
                    Normal = Bridge.ToSim(ray.normal),
                    Friction = FrictionOf(ray.collider),
                };
            }

            return Miss();
        }

        static GroundHit Miss() => new GroundHit { Hit = false };

        bool IsSelf(RaycastHit hit)
        {
            if (_self == null || hit.rigidbody != _self) return false;
            if (!_selfHitReported)
            {
                _selfHitReported = true;
                Debug.LogError($"{_self.name}: a wheel probe hit the car's own collider. Put the " +
                               "car on its own layer and clear that layer from Ground Layers on " +
                               "CarController, or the suspension reads the chassis as the road.");
            }
            return true;
        }

        /// <summary>
        /// Grip multiplier for a surface: 1 is the tarmac the tyre coefficients were
        /// measured on, lower is worse. Authored as the collider's physics material
        /// dynamic friction, so grass is a material with 0.35 rather than a lookup table
        /// of collider names.
        /// </summary>
        static float FrictionOf(Collider collider)
        {
            PhysicsMaterial material = collider != null ? collider.sharedMaterial : null;
            return material != null ? material.dynamicFriction : 1f;
        }
    }
}
