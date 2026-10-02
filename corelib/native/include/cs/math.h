/* cs/math.h -- System.Math.  Plain value functions (no references, no coost): safe for shivyc too. */
#pragma once
#include <math.h>
#include "cs/core.h"

static inline int cs_abs_i32(int v) {
    if (v == -2147483647 - 1) { cs_fail("System.OverflowException: Negating the minimum value of a twos complement number is invalid."); }
    if (v < 0) { return -v; }
    return v;
}
static inline long long cs_abs_i64(long long v) {
    if (v == -9223372036854775807LL - 1LL) { cs_fail("System.OverflowException: Negating the minimum value of a twos complement number is invalid."); }
    if (v < 0) { return -v; }
    return v;
}
static inline double cs_abs_f64(double v) { if (v < 0.0) { return -v; } return v; }
static inline int cs_max_i32(int a, int b) { if (a > b) { return a; } return b; }
static inline long long cs_max_i64(long long a, long long b) { if (a > b) { return a; } return b; }
static inline double cs_max_f64(double a, double b) { if (a > b) { return a; } return b; }
static inline int cs_min_i32(int a, int b) { if (a < b) { return a; } return b; }
static inline long long cs_min_i64(long long a, long long b) { if (a < b) { return a; } return b; }
static inline double cs_min_f64(double a, double b) { if (a < b) { return a; } return b; }
static inline int cs_sign_i32(int v) { if (v > 0) { return 1; } if (v < 0) { return -1; } return 0; }

static inline double cs_sqrt(double d) { return sqrt(d); }
static inline double cs_floor(double d) { return floor(d); }
static inline double cs_ceil(double d) { return ceil(d); }
static inline double cs_pow(double x, double y) { return pow(x, y); }
static inline double cs_sin(double a) { return sin(a); }
static inline double cs_cos(double a) { return cos(a); }
