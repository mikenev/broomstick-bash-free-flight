#!/usr/bin/env python3
"""Free Flight broom audio (original, procedural; no samples).

- SFX/broom_wind_loop.ogg  : 3.0 s mono seamless wind bed for speed-scaled volume/pitch.
  Built from circular (exactly periodic) noise through a time-varying band filter whose
  modulation is periodic in the loop length; the filter is run over three periods and the
  middle one kept, so the output is periodic and the seam is continuous. Content sits
  mostly at 150 Hz - 3 kHz so it stays airy, not hissy, at pitch 1.4 and still has body at 0.8.
- SFX/broom_turn_whoosh.ogg: soft 0.55 s one-shot swish for sharp heading changes.
Usage: python3 tools/audio/generate_wind.py [out_dir=Assets/Audio]
"""
import os, sys, subprocess, tempfile
import numpy as np
from scipy import signal

SR = 44100
OUT = sys.argv[1] if len(sys.argv) > 1 else "Assets/Audio"
rng = np.random.default_rng(2610)
LOOP_S = 3.0
WIND_RMS = 0.040        # ~-28 dBFS: at AudioSource.volume 0.8 it sits ~2 dB under in-game music (0.118*0.35)
TURN_PEAK_DB = -6.0

def periodic_noise(L, lo, hi, tilt=0.0):
    f = np.fft.rfftfreq(L, 1/SR); spec = rng.standard_normal(len(f)) + 1j*rng.standard_normal(len(f))
    w = ((f > lo) & (f < hi)) * (np.maximum(f, 1) / 500.0) ** tilt
    x = np.fft.irfft(spec * w, L); return x / x.std()

def lfo(L, cycles_amps, seed):
    r = np.random.default_rng(seed); ph = np.arange(L) / L
    return sum(a*np.sin(2*np.pi*(c*ph + r.random())) for c, a in cycles_amps)

def tv_bandpass(x, fc, q, block=128):
    """Time-varying 2nd-order bandpass (per-block coefficients, continuous state)."""
    y = np.zeros_like(x); zi = np.zeros(2)
    for i in range(0, len(x), block):
        f = float(np.clip(fc[i], 60, 8000)); bw = f / q
        b, a = signal.iirpeak(f, f/bw, fs=SR)
        y[i:i+block], zi = signal.lfilter(b, a, x[i:i+block], zi=zi)
    return y

def wind_loop():
    L = int(LOOP_S*SR)
    body = periodic_noise(L, 120, 2500, tilt=-0.6)                    # soft broadband air
    gust = 0.75 + 0.25*np.tanh(lfo(L, [(1, .6), (2, .35), (5, .2), (7, .12)], 1))
    air = periodic_noise(L, 1500, 6000, tilt=-1.0) * (0.5 + 0.5*np.tanh(lfo(L, [(3, .8), (4, .5)], 2)))
    src = np.tile(body, 3); fc = np.tile(650 * 2**(0.55*np.tanh(lfo(L, [(2, .7), (3, .5), (6, .25)], 3))), 3)
    swish = tv_bandpass(src, fc, 4.0)[L:2*L]                          # airy tonal swish (middle period)
    swish /= swish.std()
    x = body*gust + 0.45*swish*gust + 0.12*air
    # gentle 1-pole lowpass applied circularly (via FFT) to round off any edge
    f = np.fft.rfftfreq(L, 1/SR); x = np.fft.irfft(np.fft.rfft(x) / (1 + 1j*f/4500), L)
    x -= x.mean(); return x * WIND_RMS / np.sqrt(np.mean(x**2))

def turn_whoosh():
    d = 0.55; n = int(d*SR); t = np.arange(n)/n
    x = rng.standard_normal(n)
    env = np.sin(np.pi*np.clip(t/0.9, 0, 1))**2 * (1 - 0.3*t)
    fc = 350 * 2**(1.6*np.sin(np.pi*t))                               # rise-and-fall sweep ~350-1060 Hz
    y = tv_bandpass(x, fc, 2.2) + 0.25*tv_bandpass(x, fc*2.3, 3.0)
    y = signal.sosfilt(signal.butter(2, 120, 'high', fs=SR, output='sos'), y) * env
    y -= y.mean()
    a, b = int(.004*SR), int(.04*SR); y[:a] *= np.linspace(0, 1, a); y[-b:] *= np.linspace(1, 0, b)**2
    y[0] = y[-1] = 0
    return y / np.abs(y).max() * 10**(TURN_PEAK_DB/20)

def write_ogg(path, x, q):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with tempfile.NamedTemporaryFile(suffix='.f32', delete=False) as f: f.write(np.clip(x, -1, 1).astype('<f4').tobytes())
    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-f', 'f32le', '-ar', str(SR), '-ac', '1', '-i', f.name,
                    '-c:a', 'libvorbis', '-q:a', str(q), path], check=True); os.unlink(f.name)

if __name__ == '__main__':
    write_ogg(f'{OUT}/SFX/broom_wind_loop.ogg', wind_loop(), 3)
    write_ogg(f'{OUT}/SFX/broom_turn_whoosh.ogg', turn_whoosh(), 3)
