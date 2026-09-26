"""Original sample-free Pop/Electronic auditions only; no crowd or existing-file writes."""
from pathlib import Path
import hashlib
import json
import wave
import numpy as np

ROOT = Path(__file__).resolve().parent
SR = 22050
SECONDS = 16
COUNT = SR * SECONDS
BEAT = .5
RNG = np.random.default_rng(20260926)


def hz(midi):
    return 440 * 2 ** ((midi - 69) / 12)


def time(seconds):
    return np.arange(round(seconds * SR)) / SR


def envelope(t, duration, attack=.008, release=.05):
    return np.minimum(1, t / attack) * np.minimum(1, np.maximum(0, duration - t) / release)


def add(out, signal, at, gain):
    # Circular tail mixing keeps notes/reverb across the last/first bar boundary.
    indices = (round(at * SR) + np.arange(len(signal))) % COUNT
    np.add.at(out, indices, signal * gain)


def piano(midi, duration):
    t = time(duration)
    f = hz(midi)
    body = sum(gain * np.sin(2 * np.pi * f * harmonic * t) * np.exp(-t * decay)
               for harmonic, gain, decay in [(1, 1, 3.3), (2, .45, 5), (3, .18, 8), (4, .09, 12)])
    return body * envelope(t, duration, .004, .07)


def bass(midi, duration, electronic=False):
    t = time(duration)
    p = 2 * np.pi * hz(midi) * t
    body = np.sin(p) + (.34 if electronic else .13) * np.sin(2 * p) + .07 * np.sin(3 * p)
    return body * np.exp(-t * (2.8 if electronic else 2)) * envelope(t, duration)


def kick(electronic=False):
    t = time(.32)
    phase = 2 * np.pi * (45 * t + (110 if electronic else 75) * (1 - np.exp(-t * 40)) / 40)
    return np.sin(phase) * np.exp(-t * (17 if electronic else 20))


def snare():
    t = time(.19)
    noise = RNG.normal(0, 1, len(t))
    noise -= np.convolve(noise, np.ones(9) / 9, mode='same')
    return (noise * .65 + np.sin(2 * np.pi * 190 * t) * .22) * np.exp(-t * 24)


def hat(open_hat=False):
    t = time(.16 if open_hat else .065)
    noise = RNG.normal(0, 1, len(t))
    noise -= np.convolve(noise, np.ones(5) / 5, mode='same')
    return noise * np.exp(-t * (23 if open_hat else 65))


def synth(midi, duration):
    t = time(duration)
    f = hz(midi)
    # Finite additive saw-like tone; no external instrument/sample source.
    body = sum(np.sin(2 * np.pi * f * h * t) / (h ** 1.3) for h in range(1, 9))
    return body * np.exp(-t * 11) * envelope(t, duration, .005, .04)


def pad(chord, duration, pumping=False):
    t = time(duration)
    body = np.zeros(len(t))
    for midi in chord:
        f = hz(midi)
        body += np.sin(2 * np.pi * f * t) + .2 * np.sin(2 * np.pi * f * 2 * t)
    shape = envelope(t, duration, .12, .18)
    if pumping:
        shape *= .20 + .80 * (1 - np.exp(-(t % BEAT) * 8))
    return body / len(chord) * shape


def pop():
    out = np.zeros(COUNT)
    chords = [[60, 64, 67], [57, 60, 64], [53, 57, 60], [55, 59, 62]] * 2
    roots = [36, 33, 29, 31] * 2
    hook = [[76, 79, 76, 74, 72], [76, 72, 69, 72, 76],
            [77, 76, 72, 69, 72], [74, 76, 74, 71, 67],
            [76, 79, 81, 79, 76], [76, 72, 69, 72, 76],
            [77, 76, 72, 74, 76], [74, 71, 67, 71, 74]]
    for bar, chord in enumerate(chords):
        start = bar * 2
        add(out, pad(chord, 2), start, .12)
        for beat in [0, 1.5, 2.5, 3.5]:
            for n, midi in enumerate(chord):
                add(out, piano(midi + 12, .43), start + beat * BEAT + n * .009, .095)
        for beat, midi in zip([0, 1.5, 2, 3.5], [roots[bar], roots[bar] + 7, roots[bar], roots[bar] + 12]):
            add(out, bass(midi, .30), start + beat * BEAT, .28)
        for beat, midi in zip([0, .75, 1.5, 2.5, 3.25], hook[bar]):
            phrase = piano(midi, .40)
            add(out, phrase, start + beat * BEAT, .20)
            add(out, phrase, start + beat * BEAT + .18, .032)
        for beat in [0, 2, 2.75]:
            add(out, kick(), start + beat * BEAT, .39)
        for beat in [1, 3]:
            add(out, snare(), start + beat * BEAT, .18)
        for eighth in range(8):
            add(out, hat(), start + eighth * BEAT / 2, .034 if eighth % 2 == 0 else .022)
    return out


def electronic():
    out = np.zeros(COUNT)
    chords = [[57, 60, 64], [53, 57, 60], [60, 64, 67], [55, 59, 62]] * 2
    roots = [33, 29, 36, 31] * 2
    pattern = [0, 1, 2, 1, 0, 2, 1, 2, 0, 1, 2, 1, 2, 1, 0, 2]
    for bar, chord in enumerate(chords):
        start = bar * 2
        add(out, pad([m + 12 for m in chord], 2, True), start, .27)
        for beat in range(4):
            add(out, kick(True), start + beat * BEAT, .56)
            add(out, bass(roots[bar], .22, True), start + (beat + .5) * BEAT, .37)
            add(out, hat(True), start + (beat + .5) * BEAT, .064)
            if beat in [1, 3]:
                add(out, snare(), start + beat * BEAT, .11)
        for step, index in enumerate(pattern):
            phrase = synth(chord[index] + 12 + (12 if step in [7, 15] else 0), .19)
            add(out, phrase, start + step * BEAT / 4, .15 if step % 4 else .20)
            add(out, phrase, start + step * BEAT / 4 + .375, .035)
    return out


def save(name, signal):
    signal -= signal.mean()
    signal = np.tanh(signal * .8)
    signal *= .82 / max(np.max(np.abs(signal)), .0001)
    # Measured EBU R128 trims align both previews near the approved loops' loudness.
    signal *= 10 ** ({'pop_loop_v1.wav': -1.6, 'electronic_loop_v1.wav': -3.6}[name] / 20)
    # A 4 ms boundary taper protects repetition even on a sample-level restart.
    ramp = round(.004 * SR)
    signal[:ramp] *= np.linspace(0, 1, ramp)
    signal[-ramp:] *= np.linspace(1, 0, ramp)
    pcm = (np.clip(signal, -1, 1) * 32767).astype('<i2')
    dest = ROOT / name
    with wave.open(str(dest), 'wb') as stream:
        stream.setnchannels(1)
        stream.setsampwidth(2)
        stream.setframerate(SR)
        stream.writeframes(pcm.tobytes())
    with wave.open(str(dest), 'rb') as stream:
        assert (stream.getnchannels(), stream.getsampwidth(), stream.getframerate(), stream.getnframes()) == (1, 2, SR, COUNT)
    assert np.isfinite(signal).all() and pcm[0] == 0 and pcm[-1] == 0
    return {'file': name, 'seconds': SECONDS, 'sampleRate': SR, 'channels': 1, 'bits': 16,
            'peakDbfs': float(20 * np.log10(np.max(np.abs(pcm.astype(float))) / 32768)),
            'rmsDbfs': float(20 * np.log10(np.sqrt(np.mean((pcm.astype(float) / 32768) ** 2)))),
            'sha256': hashlib.sha256(dest.read_bytes()).hexdigest()}


if __name__ == '__main__':
    ROOT.mkdir(parents=True, exist_ok=True)
    results = [save('pop_loop_v1.wav', pop()), save('electronic_loop_v1.wav', electronic())]
    (ROOT / 'technical.json').write_text(json.dumps({'approval': 'preview only; pending audition', 'files': results}, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(results, indent=2))
