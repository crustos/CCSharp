/* A flat int / float interface to Box2D-Packed, for the engine's C# (unity-physics/box2d/PhysicsBackend.cs).
 *
 * Nothing here passes a struct, so the C# side never sees Box2D's bit-packed definitions (b2BodyDef, b2ShapeDef ..): the layout stays on this side
 * of the line.  Bodies and shapes are named by small integer handles; a collider is named by the id the C# gave it, which comes back in the events. */
#ifndef PB2_H
#define PB2_H

void  pb2_world(float gravity_x, float gravity_y);                  /* creates the world on the first call */
void  pb2_set_gravity(float x, float y);
void  pb2_step(float dt);

/* type: 0 dynamic, 1 kinematic, 2 static (Unity's RigidbodyType2D) */
int   pb2_body(int type, float x, float y, float angle, float vx, float vy, float gravity_scale, float damping, int freeze_rotation);
void  pb2_destroy_body(int body);
/* material: Unity's PhysicsMaterialCombine of the friction in bits 0-7 and of the bounce in bits 8-15 (Average 0, Multiply 1, Minimum 2, Maximum 3);
 * two shapes meeting use the higher mode.  A shape is named by its collider id; a collider may be several shapes (a polygon's pieces). */
int   pb2_box(int body, int collider, float half_w, float half_h, float ox, float oy, int trigger, float friction, float bounce, int material);
int   pb2_circle(int body, int collider, float radius, float ox, float oy, int trigger, float friction, float bounce, int material);
int   pb2_capsule(int body, int collider, float radius, float x1, float y1, float x2, float y2, int trigger, float friction, float bounce, int material);
/* a convex piece: pb2_poly_point for each vertex (up to 8), then pb2_poly_piece makes the shape of their hull */
void  pb2_poly_point(float x, float y);
int   pb2_poly_piece(int body, int collider, int trigger, float friction, float bounce, int material);
void  pb2_destroy_collider(int collider);

float pb2_x(int body);
float pb2_y(int body);
float pb2_vx(int body);
float pb2_vy(int body);
void  pb2_set_pos(int body, float x, float y);
void  pb2_set_velocity(int body, float vx, float vy);
float pb2_angle(int body);                                          /* radians */
void  pb2_set_angle(int body, float radians);
float pb2_angvel(int body);                                         /* radians / s */
void  pb2_set_angvel(int body, float radians_per_second);
void  pb2_torque(int body, float t);
void  pb2_angular_impulse(int body, float i);
void  pb2_set_freeze(int body, int freeze_rotation);
void  pb2_force(int body, float fx, float fy);
void  pb2_impulse(int body, float ix, float iy);
void  pb2_set_type(int body, int type);
void  pb2_set_mass(int body, float mass);                           /* the shapes' mass data scaled to this mass */
void  pb2_set_gravity_scale(int body, float scale);
void  pb2_set_damping(int body, float damping);
void  pb2_set_enabled(int body, int enabled);

/* The touch events of the last step.  kind: 0 contact begins, 1 contact ends, 2 trigger overlap begins, 3 trigger overlap ends.  a and b are collider ids. */
int   pb2_event_count(void);
int   pb2_event_kind(int i);
int   pb2_event_a(int i);
int   pb2_event_b(int i);

/* Joints.  Set the parameters the kind uses with pb2_jparam (indices below, Unity's units: degrees, metres, the anchors in the body's local space), then pb2_joint
 * builds it.  kind: 0 hinge, 1 distance, 2 spring, 3 fixed, 4 slider, 5 wheel, 6 friction, 7 relative, 8 target.  `other` is -1 for the ground.  Returns a handle. */
enum {
	PB2_J_AUTO_ANCHOR, PB2_J_ANCHOR_X, PB2_J_ANCHOR_Y, PB2_J_CANCHOR_X, PB2_J_CANCHOR_Y, PB2_J_COLLIDE, PB2_J_USE_LIMITS, PB2_J_LOWER, PB2_J_UPPER,
	PB2_J_USE_MOTOR, PB2_J_MOTOR_SPEED, PB2_J_MOTOR_MAX, PB2_J_DISTANCE, PB2_J_AUTO_DISTANCE, PB2_J_MAX_DIST_ONLY, PB2_J_FREQUENCY, PB2_J_DAMPING,
	PB2_J_ANGLE, PB2_J_AUTO_ANGLE, PB2_J_MAX_FORCE, PB2_J_MAX_TORQUE, PB2_J_CORRECTION, PB2_J_AUTO_OFFSET, PB2_J_OFFSET_X, PB2_J_OFFSET_Y,
	PB2_J_OFFSET_ANGLE, PB2_J_AUTO_TARGET, PB2_J_TARGET_X, PB2_J_TARGET_Y, PB2_J_COUNT
};
void  pb2_jparam(int index, float value);
int   pb2_joint(int kind, int own, int other);
void  pb2_destroy_joint(int joint);

#endif
