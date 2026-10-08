// BroadLeafV1, profile version 1. Keep the material map identical to Agro/Plant/Leaf/ContactDeformation.cs.
// Rest assets carry the relief at the shared midrib in uv.x; this is a geometry binding, not a texture coordinate.
import { Mat4 } from './Mat4';

export type Fold = { angles: Float32Array; scale: Float32Array };

export function deformLeaf(out: { x: number; y: number; z: number }, x: number, y: number, z: number,
                           hinge: number, q: ArrayLike<number>, s: ArrayLike<number>) {
    if (!(q[0] || q[1] || q[2] || q[3]) || s[0] <= 0 || s[1] <= 0 || s[2] <= 0) {
        out.x = x; out.y = y; out.z = z; return out;
    }
    const px = (x + 1) * s[0], py = z * s[2], pz = -y * s[1], height = hinge * s[2];
    const side = pz >= 0 ? 1 : -1, fold = pz >= 0 ? q[2] : q[3];
    const fy = height + (py - height) * Math.cos(fold) + Math.abs(pz) * Math.sin(fold);
    const fz = pz * Math.cos(fold) - side * (py - height) * Math.sin(fold);
    const length = 2 * s[0], third = length / 3, t = px / length;
    let cx = Math.min(px, third), cy = 0, angle = 0;
    for (let section = 0; section < 2; ++section) {
        if (t <= (section + 1) / 3) continue;
        const f = Math.max(0, Math.min(1, 3 * t - section - 1)), a = q[section] * f, l = third * f;
        const a2 = a * a;
        const ax = Math.abs(a) < .25 ? l * (1 - a2 / 6 + a2*a2 / 120 - a2*a2*a2 / 5040) : l * Math.sin(a) / a;
        const ay = Math.abs(a) < .25 ? l * a * (.5 - a2 / 24 + a2*a2 / 720 - a2*a2*a2 / 40320) : l * (1 - Math.cos(a)) / a;
        const start = section === 0 ? 0 : q[0];
        cx += Math.cos(start) * ax - Math.sin(start) * ay;
        cy += Math.sin(start) * ax + Math.cos(start) * ay;
        angle = start + a;
    }
    if (t > 1) { cx += (t - 1) * length * Math.cos(angle); cy += (t - 1) * length * Math.sin(angle); }
    out.x = (cx - fy * Math.sin(angle)) / s[0] - 1;
    out.y = -fz / s[1];
    out.z = (cy + fy * Math.cos(angle)) / s[2];
    return out;
}

export function foldScale(model: Mat4): Float32Array {
    return new Float32Array([Math.hypot(model[0], model[1], model[2]),
        Math.hypot(model[4], model[5], model[6]), Math.hypot(model[8], model[9], model[10]), 1]);
}

// The shader ports are deliberately kept together and checked against exported C# vertices.
export const FOLD_GLSL = `
// Bounded angles permit accurate polynomials. GPU trig accuracy near zero is not
// sufficient for the material map's subtraction and division operations.
float foldSin(float a) {float x=a*a;return a*(1.0+x*(-1.0/6.0+x*(1.0/120.0+x*(-1.0/5040.0+x/362880.0))));}
float foldCos(float a) {float x=a*a;return 1.0+x*(-0.5+x*(1.0/24.0+x*(-1.0/720.0+x*(1.0/40320.0-x/3628800.0))));}
vec2 foldArc(float l, float a) {
  float a2=a*a;
  if(abs(a)<0.25) return vec2(l*(1.0-a2/6.0+a2*a2/120.0-a2*a2*a2/5040.0),l*a*(0.5-a2/24.0+a2*a2/720.0-a2*a2*a2/40320.0));
  return vec2(l*foldSin(a)/a,l*(1.0-foldCos(a))/a);
}
vec2 foldRotate(vec2 p,float a) { return vec2(foldCos(a)*p.x-foldSin(a)*p.y,foldSin(a)*p.x+foldCos(a)*p.y); }
vec3 foldLeaf(vec3 raw,float hinge,vec4 q,vec3 s) {
  if(dot(abs(q),vec4(1.0))==0.0 || min(s.x,min(s.y,s.z))<=0.0) return raw;
  vec3 p=vec3((raw.x+1.0)*s.x,raw.z*s.z,-raw.y*s.y);
  float h=hinge*s.z;float side=p.z>=0.0?1.0:-1.0;float fold=p.z>=0.0?q.z:q.w;
  float fy=h+(p.y-h)*foldCos(fold)+abs(p.z)*foldSin(fold);
  float fz=p.z*foldCos(fold)-side*(p.y-h)*foldSin(fold);
  float len=2.0*s.x;float third=len/3.0;float t=p.x/len;float angle=0.0;
  vec2 centre=vec2(min(p.x,third),0.0);
  if(t>1.0/3.0){float f=clamp(3.0*t-1.0,0.0,1.0);centre+=foldArc(third*f,q.x*f);angle=q.x*f;}
  if(t>2.0/3.0){float f=clamp(3.0*t-2.0,0.0,1.0);centre+=foldRotate(foldArc(third*f,q.y*f),q.x);angle=q.x+q.y*f;}
  if(t>1.0)centre+=foldRotate(vec2((t-1.0)*len,0.0),angle);
  centre+=foldRotate(vec2(0.0,fy),angle);
  return vec3(centre.x/s.x-1.0,-fz/s.y,centre.y/s.z);
}`;

export const FOLD_WGSL = `
fn foldSin(a:f32)->f32 {let x=a*a;return a*(1.0+x*(-1.0/6.0+x*(1.0/120.0+x*(-1.0/5040.0+x/362880.0))));}
fn foldCos(a:f32)->f32 {let x=a*a;return 1.0+x*(-0.5+x*(1.0/24.0+x*(-1.0/720.0+x*(1.0/40320.0-x/3628800.0))));}
fn foldArc(l:f32,a:f32)->vec2f {
  let a2=a*a;
  if(abs(a)<0.25){return vec2f(l*(1.0-a2/6.0+a2*a2/120.0-a2*a2*a2/5040.0),l*a*(0.5-a2/24.0+a2*a2/720.0-a2*a2*a2/40320.0));}
  return vec2f(l*foldSin(a)/a,l*(1.0-foldCos(a))/a);
}
fn foldRotate(p:vec2f,a:f32)->vec2f{return vec2f(foldCos(a)*p.x-foldSin(a)*p.y,foldSin(a)*p.x+foldCos(a)*p.y);}
fn foldLeaf(raw:vec3f,hinge:f32,q:vec4f,s:vec3f)->vec3f {
  if(dot(abs(q),vec4f(1.0))==0.0 || min(s.x,min(s.y,s.z))<=0.0){return raw;}
  let p=vec3f((raw.x+1.0)*s.x,raw.z*s.z,-raw.y*s.y);
  let h=hinge*s.z;let side=select(-1.0,1.0,p.z>=0.0);let fold=select(q.w,q.z,p.z>=0.0);
  let fy=h+(p.y-h)*foldCos(fold)+abs(p.z)*foldSin(fold);
  let fz=p.z*foldCos(fold)-side*(p.y-h)*foldSin(fold);
  let len=2.0*s.x;let third=len/3.0;let t=p.x/len;var angle=0.0;
  var centre=vec2f(min(p.x,third),0.0);
  if(t>1.0/3.0){let f=clamp(3.0*t-1.0,0.0,1.0);centre+=foldArc(third*f,q.x*f);angle=q.x*f;}
  if(t>2.0/3.0){let f=clamp(3.0*t-2.0,0.0,1.0);centre+=foldRotate(foldArc(third*f,q.y*f),q.x);angle=q.x+q.y*f;}
  if(t>1.0){centre+=foldRotate(vec2f((t-1.0)*len,0.0),angle);}
  centre+=foldRotate(vec2f(0.0,fy),angle);
  return vec3f(centre.x/s.x-1.0,-fz/s.y,centre.y/s.z);
}`;

/** Three mechanical cylinders share one biological/picking identity. */
export function petioleSpan(out: Mat4, transform: Float32Array, length: number, radius: number,
                            q: ArrayLike<number>, span: number) {
    const rotate = (x: number, y: number, z: number, part: number): number[] => {
        const roll = q[part * 2], yaw = q[part * 2 + 1];
        const a = Math.cos(roll) * x - Math.sin(roll) * y, b = Math.sin(roll) * x + Math.cos(roll) * y;
        return [Math.cos(yaw) * a + Math.sin(yaw) * z, b, -Math.sin(yaw) * a + Math.cos(yaw) * z];
    };
    const direction = (x: number, y: number, z: number, part: number) => {
        let p = [x, y, z];
        if (part === 2) p = rotate(p[0], p[1], p[2], 1);
        if (part >= 1) p = rotate(p[0], p[1], p[2], 0);
        return [transform[1] * p[0] + transform[2] * p[1] + transform[0] * p[2],
            transform[5] * p[0] + transform[6] * p[1] + transform[4] * p[2],
            transform[9] * p[0] + transform[10] * p[1] + transform[8] * p[2]];
    };
    const x = direction(1, 0, 0, span), y = direction(0, 1, 0, span), z = direction(0, 0, 1, span);
    const base = [transform[3], transform[7], transform[11]], third = length / 3;
    for (let i = 0; i < span; ++i) { const d = direction(1, 0, 0, i); for (let k = 0; k < 3; ++k) base[k] += d[k] * third; }
    for (let k = 0; k < 3; ++k) {
        out[k] = z[k] * radius; out[4 + k] = x[k] * third; out[8 + k] = y[k] * radius;
        out[12 + k] = base[k] + x[k] * third * .5;
    }
    out[3] = out[7] = out[11] = 0; out[15] = 1;
}
