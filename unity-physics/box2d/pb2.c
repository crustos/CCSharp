/* See pb2.h. */
#include "pb2.h"
#include "box2d/box2d.h"
#include <stdint.h>
#include <math.h>

enum { PB2_MAX_BODIES = 4096, PB2_MAX_SHAPES = 8192, PB2_MAX_EVENTS = 8192, PB2_SUB_STEPS = 4 };

static int pb2_ready;
static b2WorldId pb2_w;
static b2BodyId pb2_bodies[PB2_MAX_BODIES];
static unsigned char pb2_body_used[PB2_MAX_BODIES];
static b2ShapeId pb2_shapes[PB2_MAX_SHAPES];
static int pb2_shape_collider[PB2_MAX_SHAPES];
static unsigned char pb2_shape_used[PB2_MAX_SHAPES];
static int pb2_ev_kind[PB2_MAX_EVENTS];
static int pb2_ev_a[PB2_MAX_EVENTS];
static int pb2_ev_b[PB2_MAX_EVENTS];
static int pb2_ev_n;

static b2BodyType pb2_type(int t)
{
	return t == 2 ? b2_staticBody : t == 1 ? b2_kinematicBody : b2_dynamicBody;
}

/* PhysicsMaterialCombine: Average 0, Multiply 1, Minimum 2, Maximum 3; the higher mode wins */
static float pb2_combine(float a, float b, int ca, int cb)
{
	int mode = ca > cb ? ca : cb;
	if (mode == 1)
		return a * b;
	if (mode == 2)
		return a < b ? a : b;
	if (mode == 3)
		return a > b ? a : b;
	return 0.5f * (a + b);
}

static float pb2_friction(float a, uint64_t ma, float b, uint64_t mb)
{
	return pb2_combine(a, b, (int)(ma & 0xff), (int)(mb & 0xff));
}

static float pb2_restitution(float a, uint64_t ma, float b, uint64_t mb)
{
	return pb2_combine(a, b, (int)((ma >> 8) & 0xff), (int)((mb >> 8) & 0xff));
}

void pb2_world(float gx, float gy)
{
	if (pb2_ready)
		return;
	b2WorldDef def = b2DefaultWorldDef();
	def.gravity = (b2Vec2){ gx, gy };
	def.frictionCallback = pb2_friction;
	def.restitutionCallback = pb2_restitution;
	pb2_w = b2CreateWorld(&def);
	pb2_ready = 1;
}

void pb2_set_gravity(float x, float y)
{
	b2World_SetGravity(pb2_w, (b2Vec2){ x, y });
}

int pb2_body(int type, float x, float y, float angle, float vx, float vy, float gravity_scale, float damping, int freeze_rotation)
{
	int h = 0;
	while (h < PB2_MAX_BODIES && pb2_body_used[h])
		h++;
	if (h == PB2_MAX_BODIES)
		return -1;
	b2BodyDef def = b2DefaultBodyDef();
	def.type = pb2_type(type);
	def.position = (b2Pos){ x, y };
	def.rotation = b2MakeRot(angle);
	def.linearVelocity = (b2Vec2){ vx, vy };
	def.gravityScale = gravity_scale;
	def.linearDamping = damping;
	def.motionLocks.angularZ = freeze_rotation != 0 || type == 2;
	pb2_bodies[h] = b2CreateBody(pb2_w, &def);
	pb2_body_used[h] = 1;
	return h;
}

void pb2_destroy_body(int h)
{
	if (h < 0 || !pb2_body_used[h])
		return;
	/* its shapes go with it: forget their handles */
	for (int s = 0; s < PB2_MAX_SHAPES; ++s)
	{
		if (pb2_shape_used[s] && B2_ID_EQUALS(b2Shape_GetBody(pb2_shapes[s]), pb2_bodies[h]))
			pb2_shape_used[s] = 0;
	}
	b2DestroyBody(pb2_bodies[h]);
	pb2_body_used[h] = 0;
}

static int pb2_new_shape(b2ShapeId id, int collider)
{
	int s = 0;
	while (s < PB2_MAX_SHAPES && pb2_shape_used[s])
		s++;
	if (s == PB2_MAX_SHAPES)
		return -1;
	pb2_shapes[s] = id;
	pb2_shape_collider[s] = collider;
	pb2_shape_used[s] = 1;
	return s;
}

static b2ShapeDef pb2_shape_def(int collider, int trigger, float friction, float bounce, int material)
{
	b2ShapeDef def = b2DefaultShapeDef();
	def.userData = (void*)(intptr_t)(collider + 1);
	def.material.friction = friction;
	def.material.restitution = bounce;
	def.material.userMaterialId = (uint64_t)(material & 0xffff);
	def.isSensor = trigger != 0;
	def.enableSensorEvents = true;
	def.enableContactEvents = trigger == 0;
	return def;
}

int pb2_box(int body, int collider, float hw, float hh, float ox, float oy, int trigger, float friction, float bounce, int material)
{
	if (body < 0 || !pb2_body_used[body])
		return -1;
	b2ShapeDef def = pb2_shape_def(collider, trigger, friction, bounce, material);
	b2Rot rot = { 1.0f, 0.0f };
	b2Polygon box = b2MakeOffsetBox(hw, hh, (b2Vec2){ ox, oy }, rot);
	return pb2_new_shape(b2CreatePolygonShape(pb2_bodies[body], &def, &box), collider);
}

int pb2_circle(int body, int collider, float radius, float ox, float oy, int trigger, float friction, float bounce, int material)
{
	if (body < 0 || !pb2_body_used[body])
		return -1;
	b2ShapeDef def = pb2_shape_def(collider, trigger, friction, bounce, material);
	b2Circle c = { { ox, oy }, radius };
	return pb2_new_shape(b2CreateCircleShape(pb2_bodies[body], &def, &c), collider);
}

int pb2_capsule(int body, int collider, float radius, float x1, float y1, float x2, float y2, int trigger, float friction, float bounce, int material)
{
	if (body < 0 || !pb2_body_used[body])
		return -1;
	b2ShapeDef def = pb2_shape_def(collider, trigger, friction, bounce, material);
	b2Capsule c = { { x1, y1 }, { x2, y2 }, radius };
	return pb2_new_shape(b2CreateCapsuleShape(pb2_bodies[body], &def, &c), collider);
}

static b2Vec2 pb2_pts[B2_MAX_POLYGON_VERTICES];
static int pb2_pts_n;

void pb2_poly_point(float x, float y)
{
	if (pb2_pts_n < B2_MAX_POLYGON_VERTICES)
		pb2_pts[pb2_pts_n++] = (b2Vec2){ x, y };
}

int pb2_poly_piece(int body, int collider, int trigger, float friction, float bounce, int material)
{
	int n = pb2_pts_n;
	pb2_pts_n = 0;
	if (body < 0 || !pb2_body_used[body] || n < 3)
		return -1;
	b2Hull hull = b2ComputeHull(pb2_pts, n);
	if (hull.count == 0)
		return -1;
	b2ShapeDef def = pb2_shape_def(collider, trigger, friction, bounce, material);
	b2Polygon poly = b2MakePolygon(&hull, 0.0f);
	return pb2_new_shape(b2CreatePolygonShape(pb2_bodies[body], &def, &poly), collider);
}

void pb2_destroy_collider(int collider)
{
	for (int s = 0; s < PB2_MAX_SHAPES; ++s)
	{
		if (pb2_shape_used[s] && pb2_shape_collider[s] == collider)
		{
			b2DestroyShape(pb2_shapes[s], true);
			pb2_shape_used[s] = 0;
		}
	}
}

float pb2_x(int h) { return (float)b2Body_GetPosition(pb2_bodies[h]).x; }
float pb2_y(int h) { return (float)b2Body_GetPosition(pb2_bodies[h]).y; }
float pb2_vx(int h) { return b2Body_GetLinearVelocity(pb2_bodies[h]).x; }
float pb2_vy(int h) { return b2Body_GetLinearVelocity(pb2_bodies[h]).y; }

void pb2_set_pos(int h, float x, float y)
{
	b2Body_SetTransform(pb2_bodies[h], (b2Pos){ x, y }, b2Body_GetRotation(pb2_bodies[h]));
}

float pb2_angle(int h) { return b2Rot_GetAngle(b2Body_GetRotation(pb2_bodies[h])); }
void pb2_set_angle(int h, float a) { b2Body_SetTransform(pb2_bodies[h], b2Body_GetPosition(pb2_bodies[h]), b2MakeRot(a)); }
float pb2_angvel(int h) { return b2Body_GetAngularVelocity(pb2_bodies[h]); }
void pb2_set_angvel(int h, float w) { b2Body_SetAngularVelocity(pb2_bodies[h], w); }
void pb2_torque(int h, float t) { b2Body_ApplyTorque(pb2_bodies[h], t, true); }
void pb2_angular_impulse(int h, float i) { b2Body_ApplyAngularImpulse(pb2_bodies[h], i, true); }

void pb2_set_freeze(int h, int on)
{
	b2MotionLocks locks = { 0 };
	locks.angularZ = on != 0;
	b2Body_SetMotionLocks(pb2_bodies[h], locks);
}

void pb2_set_velocity(int h, float vx, float vy) { b2Body_SetLinearVelocity(pb2_bodies[h], (b2Vec2){ vx, vy }); }
void pb2_force(int h, float fx, float fy) { b2Body_ApplyForceToCenter(pb2_bodies[h], (b2Vec2){ fx, fy }, true); }
void pb2_impulse(int h, float ix, float iy) { b2Body_ApplyLinearImpulseToCenter(pb2_bodies[h], (b2Vec2){ ix, iy }, true); }
void pb2_set_type(int h, int type) { b2Body_SetType(pb2_bodies[h], pb2_type(type)); }
void pb2_set_gravity_scale(int h, float s) { b2Body_SetGravityScale(pb2_bodies[h], s); }
void pb2_set_damping(int h, float d) { b2Body_SetLinearDamping(pb2_bodies[h], d); }

void pb2_set_enabled(int h, int on)
{
	if (on)
		b2Body_Enable(pb2_bodies[h]);
	else
		b2Body_Disable(pb2_bodies[h]);
}

void pb2_set_mass(int h, float mass)
{
	b2MassData md = b2Body_GetMassData(pb2_bodies[h]);
	if (mass <= 0.0f)
		return;
	if (md.mass > 0.0f)
		md.rotationalInertia *= mass / md.mass;
	else
		md.rotationalInertia = mass;                    /* a body with no shapes: Unity still gives it its mass */
	md.mass = mass;
	b2Body_SetMassData(pb2_bodies[h], md);
}

static int pb2_collider_of(b2ShapeId s)
{
	if (!b2Shape_IsValid(s))
		return -1;
	return (int)(intptr_t)b2Shape_GetUserData(s) - 1;
}

static void pb2_push(int kind, b2ShapeId a, b2ShapeId b)
{
	int ca = pb2_collider_of(a);
	int cb = pb2_collider_of(b);
	if (ca < 0 || cb < 0 || pb2_ev_n >= PB2_MAX_EVENTS)
		return;
	pb2_ev_kind[pb2_ev_n] = kind;
	pb2_ev_a[pb2_ev_n] = ca;
	pb2_ev_b[pb2_ev_n] = cb;
	pb2_ev_n++;
}

void pb2_step(float dt)
{
	b2World_Step(pb2_w, dt, PB2_SUB_STEPS);
	pb2_ev_n = 0;
	b2ContactEvents ce = b2World_GetContactEvents(pb2_w);
	for (int i = 0; i < ce.beginCount; ++i)
		pb2_push(0, ce.beginEvents[i].shapeIdA, ce.beginEvents[i].shapeIdB);
	for (int i = 0; i < ce.endCount; ++i)
		pb2_push(1, ce.endEvents[i].shapeIdA, ce.endEvents[i].shapeIdB);
	b2SensorEvents se = b2World_GetSensorEvents(pb2_w);
	for (int i = 0; i < se.beginCount; ++i)
		pb2_push(2, se.beginEvents[i].sensorShapeId, se.beginEvents[i].visitorShapeId);
	for (int i = 0; i < se.endCount; ++i)
		pb2_push(3, se.endEvents[i].sensorShapeId, se.endEvents[i].visitorShapeId);
}

int pb2_event_count(void) { return pb2_ev_n; }
int pb2_event_kind(int i) { return pb2_ev_kind[i]; }
int pb2_event_a(int i) { return pb2_ev_a[i]; }
int pb2_event_b(int i) { return pb2_ev_b[i]; }


/* ---- joints, as box2d_unity.py builds them from unity_pack's tables */

enum { PB2_MAX_JOINTS = 1024 };
#define PB2_DEG (B2_PI / 180.0f)
static float pb2_jp[PB2_J_COUNT];
static b2JointId pb2_joints[PB2_MAX_JOINTS];
static unsigned char pb2_joint_used[PB2_MAX_JOINTS];
static b2BodyId pb2_ground;
static int pb2_ground_ready;

void pb2_jparam(int i, float v) { if (i >= 0 && i < PB2_J_COUNT) pb2_jp[i] = v; }

static void pb2_base(b2JointDef* base, b2BodyId a, b2Vec2 pa, float qa, b2BodyId b, b2Vec2 pb, float qb)
{
	base->bodyIdA = a;
	base->bodyIdB = b;
	base->localFrameA.p = pa;
	base->localFrameA.q = b2MakeRot(qa);
	base->localFrameB.p = pb;
	base->localFrameB.q = b2MakeRot(qb);
	base->collideConnected = pb2_jp[PB2_J_COLLIDE] != 0.0f;
}

int pb2_joint(int kind, int ownH, int otherH)
{
	if (ownH < 0 || !pb2_body_used[ownH])
		return -1;
	if (!pb2_ground_ready)
	{
		b2BodyDef gd = b2DefaultBodyDef();
		gd.type = b2_staticBody;
		pb2_ground = b2CreateBody(pb2_w, &gd);
		pb2_ground_ready = 1;
	}
	b2BodyId own = pb2_bodies[ownH];
	b2BodyId other = otherH >= 0 && pb2_body_used[otherH] ? pb2_bodies[otherH] : pb2_ground;
	float* p = pb2_jp;
	b2Vec2 la = { p[PB2_J_ANCHOR_X], p[PB2_J_ANCHOR_Y] };
	b2Pos wa = b2Body_GetWorldPoint(own, la);
	b2Vec2 lc = p[PB2_J_AUTO_ANCHOR] != 0.0f ? b2Body_GetLocalPoint(other, wa) : (b2Vec2){ p[PB2_J_CANCHOR_X], p[PB2_J_CANCHOR_Y] };
	b2Pos wc = b2Body_GetWorldPoint(other, lc);
	float angOwn = b2Rot_GetAngle(b2Body_GetRotation(own));
	float angOther = b2Rot_GetAngle(b2Body_GetRotation(other));
	float dx = (float)(wa.x - wc.x), dy = (float)(wa.y - wc.y);
	float upper = p[PB2_J_UPPER] > p[PB2_J_LOWER] ? p[PB2_J_UPPER] : p[PB2_J_LOWER];
	b2JointId id = b2_nullJointId;
	switch (kind)
	{
		case 0: /* hinge */
		{
			b2RevoluteJointDef def = b2DefaultRevoluteJointDef();
			pb2_base(&def.base, other, lc, angOwn - angOther, own, la, 0.0f);
			def.enableLimit = p[PB2_J_USE_LIMITS] != 0.0f;
			def.lowerAngle = p[PB2_J_LOWER] * PB2_DEG;
			def.upperAngle = upper * PB2_DEG;
			def.enableMotor = p[PB2_J_USE_MOTOR] != 0.0f;
			def.motorSpeed = p[PB2_J_MOTOR_SPEED] * PB2_DEG;
			def.maxMotorTorque = p[PB2_J_MOTOR_MAX];
			id = b2CreateRevoluteJoint(pb2_w, &def);
			break;
		}
		case 1: /* distance */
		case 2: /* spring */
		{
			b2DistanceJointDef def = b2DefaultDistanceJointDef();
			pb2_base(&def.base, other, lc, 0.0f, own, la, 0.0f);
			float length = p[PB2_J_AUTO_DISTANCE] != 0.0f ? sqrtf(dx * dx + dy * dy) : p[PB2_J_DISTANCE];
			if (length < 0.005f)
				length = 0.005f;
			def.length = length;
			def.minLength = 0.0f;
			def.maxLength = length;
			if (kind == 2)
			{
				def.enableSpring = p[PB2_J_FREQUENCY] > 0.0f;
				def.hertz = p[PB2_J_FREQUENCY];
				def.dampingRatio = p[PB2_J_DAMPING];
			}
			else if (p[PB2_J_MAX_DIST_ONLY] != 0.0f)
			{
				def.enableSpring = true;                /* a rope: slack up to the distance, taut at it */
				def.hertz = 0.0f;
				def.enableLimit = true;
			}
			id = b2CreateDistanceJoint(pb2_w, &def);
			break;
		}
		case 3: /* fixed */
		{
			b2WeldJointDef def = b2DefaultWeldJointDef();
			pb2_base(&def.base, other, lc, angOwn - angOther, own, la, 0.0f);
			def.linearHertz = p[PB2_J_FREQUENCY];
			def.angularHertz = p[PB2_J_FREQUENCY];
			def.linearDampingRatio = p[PB2_J_DAMPING];
			def.angularDampingRatio = p[PB2_J_DAMPING];
			id = b2CreateWeldJoint(pb2_w, &def);
			break;
		}
		case 4: /* slider: the axis in the world, degrees */
		{
			float angle = p[PB2_J_ANGLE];
			if (p[PB2_J_AUTO_ANGLE] != 0.0f && dx * dx + dy * dy > 1.0e-12f)
				angle = atan2f(dy, dx) / PB2_DEG;
			float axis = angle * PB2_DEG;
			b2PrismaticJointDef def = b2DefaultPrismaticJointDef();
			pb2_base(&def.base, other, lc, axis - angOther, own, la, axis - angOwn);
			def.enableLimit = p[PB2_J_USE_LIMITS] != 0.0f;
			def.lowerTranslation = p[PB2_J_LOWER];
			def.upperTranslation = upper;
			def.enableMotor = p[PB2_J_USE_MOTOR] != 0.0f;
			def.motorSpeed = p[PB2_J_MOTOR_SPEED];
			def.maxMotorForce = p[PB2_J_MOTOR_MAX];
			id = b2CreatePrismaticJoint(pb2_w, &def);
			break;
		}
		case 5: /* wheel: on the chassis, connected to the wheel */
		{
			float axis = p[PB2_J_ANGLE] * PB2_DEG;
			b2WheelJointDef def = b2DefaultWheelJointDef();
			pb2_base(&def.base, own, la, axis - angOwn, other, lc, axis - angOther);
			def.enableSpring = p[PB2_J_FREQUENCY] > 0.0f;
			def.hertz = p[PB2_J_FREQUENCY];
			def.dampingRatio = p[PB2_J_DAMPING];
			def.enableLimit = p[PB2_J_USE_LIMITS] != 0.0f;
			def.lowerTranslation = p[PB2_J_LOWER];
			def.upperTranslation = upper;
			def.enableMotor = p[PB2_J_USE_MOTOR] != 0.0f;
			def.motorSpeed = p[PB2_J_MOTOR_SPEED] * PB2_DEG;
			def.maxMotorTorque = p[PB2_J_MOTOR_MAX];
			id = b2CreateWheelJoint(pb2_w, &def);
			break;
		}
		case 6: /* friction: relative motion to rest, up to maxForce / maxTorque */
		{
			b2MotorJointDef def = b2DefaultMotorJointDef();
			pb2_base(&def.base, other, lc, angOwn - angOther, own, la, 0.0f);
			def.maxVelocityForce = p[PB2_J_MAX_FORCE];
			def.maxVelocityTorque = p[PB2_J_MAX_TORQUE];
			def.linearHertz = 0.0f;
			def.angularHertz = 0.0f;
			def.maxSpringForce = 0.0f;
			def.maxSpringTorque = 0.0f;
			id = b2CreateMotorJoint(pb2_w, &def);
			break;
		}
		case 7: /* relative: the own body held at an offset from the connected one */
		{
			float ox = p[PB2_J_OFFSET_X], oy = p[PB2_J_OFFSET_Y], oa = p[PB2_J_OFFSET_ANGLE];
			if (p[PB2_J_AUTO_OFFSET] != 0.0f)
			{
				b2Vec2 off = b2Body_GetLocalPoint(other, b2Body_GetPosition(own));
				ox = off.x;
				oy = off.y;
				oa = (angOwn - angOther) / PB2_DEG;
			}
			/* correctionScale as a spring: sqrt(scale) / (2 pi dt) hertz */
			float c = p[PB2_J_CORRECTION] > 0.0f ? p[PB2_J_CORRECTION] : 0.0f;
			float hz = sqrtf(c) / (2.0f * B2_PI * 0.02f);
			b2MotorJointDef def = b2DefaultMotorJointDef();
			pb2_base(&def.base, other, (b2Vec2){ ox, oy }, oa * PB2_DEG, own, (b2Vec2){ 0.0f, 0.0f }, 0.0f);
			def.maxVelocityForce = p[PB2_J_MAX_FORCE];
			def.maxVelocityTorque = p[PB2_J_MAX_TORQUE];
			def.linearHertz = hz;
			def.angularHertz = hz;
			def.linearDampingRatio = 1.0f;
			def.angularDampingRatio = 1.0f;
			def.maxSpringForce = p[PB2_J_MAX_FORCE];
			def.maxSpringTorque = p[PB2_J_MAX_TORQUE];
			id = b2CreateMotorJoint(pb2_w, &def);
			break;
		}
		case 8: /* target: the own anchor pulled to a world point by a spring */
		{
			float tx = p[PB2_J_TARGET_X], ty = p[PB2_J_TARGET_Y];
			if (p[PB2_J_AUTO_TARGET] != 0.0f)
			{
				tx = (float)wa.x;
				ty = (float)wa.y;
			}
			b2MotorJointDef def = b2DefaultMotorJointDef();
			pb2_base(&def.base, pb2_ground, (b2Vec2){ tx, ty }, angOwn, own, la, 0.0f);
			def.maxVelocityForce = 0.0f;
			def.maxVelocityTorque = 0.0f;
			def.linearHertz = p[PB2_J_FREQUENCY];
			def.linearDampingRatio = p[PB2_J_DAMPING];
			def.maxSpringForce = p[PB2_J_MAX_FORCE];
			def.angularHertz = 0.0f;
			def.maxSpringTorque = 0.0f;
			id = b2CreateMotorJoint(pb2_w, &def);
			break;
		}
		default:
			break;
	}
	if (B2_IS_NULL(id))
		return -1;
	int j = 0;
	while (j < PB2_MAX_JOINTS && pb2_joint_used[j])
		j++;
	if (j == PB2_MAX_JOINTS)
		return -1;
	pb2_joints[j] = id;
	pb2_joint_used[j] = 1;
	return j;
}

void pb2_destroy_joint(int j)
{
	if (j < 0 || !pb2_joint_used[j])
		return;
	if (b2Joint_IsValid(pb2_joints[j]))              /* destroying a body destroys its joints */
		b2DestroyJoint(pb2_joints[j]);
	pb2_joint_used[j] = 0;
}
