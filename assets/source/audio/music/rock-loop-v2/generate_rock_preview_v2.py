"""Original sample-free Rock audition; writes only this local preview folder."""
from pathlib import Path
import hashlib
import json
import wave
import numpy as np

ROOT = Path(__file__).resolve().parent
SR = 22050
BEAT = 60 / 115
COUNT = round(SR * 8 * 4 * BEAT)  # Eight whole 4/4 bars at 115 BPM.
RNG = np.random.default_rng(2026092601)
MASTER_TRIM_DB = -1.1  # EBU R128 measured trim to match other previews near -18.5 LUFS.


def hz(midi):
    return 440 * 2 ** ((midi - 69) / 12)


def add(out, signal, at, gain):
    indices = (round(at * SR) + np.arange(len(signal))) % COUNT
    np.add.at(out, indices, signal * gain)


def guitar(midi, duration, muted=False):
    count = round(duration * SR)
    delay = round(SR / hz(midi))
    excitation = RNG.uniform(-1, 1, delay)
    excitation = (excitation + np.roll(excitation, 1)) / 2
    result = np.zeros(count)
    for i in range(count):
        slot = i % delay
        value = excitation[slot]
        result[i] = value
        excitation[slot] = .998 * (.67 * value + .33 * excitation[(slot + 1) % delay])
    t = np.arange(count) / SR
    # Saturated string excitation followed by cabinet-like high-frequency softening.
    result = np.tanh(result * 7)
    result = np.convolve(result, np.ones(5) / 5, mode='same')
    envelope = np.minimum(1, t / .003) * np.minimum(1, np.maximum(0, duration - t) / .045)
    envelope *= np.exp(-t * (12 if muted else 2.2))
    return result * envelope


def bass(midi, duration):
    t = np.arange(round(duration * SR)) / SR
    phase = 2 * np.pi * hz(midi) * t
    tone = np.sin(phase) + .28 * np.sin(2 * phase) + .08 * np.sin(3 * phase)
    envelope = np.minimum(1, t / .008) * np.minimum(1, np.maximum(0, duration - t) / .04)
    return np.tanh(tone * 1.2) * np.exp(-t * 2.1) * envelope


def kick():
    t = np.arange(round(.36 * SR)) / SR
    phase = 2 * np.pi * (44 * t + 85 * (1 - np.exp(-t * 32)) / 32)
    return np.sin(phase) * np.exp(-t * 16)


def snare():
    t = np.arange(round(.26 * SR)) / SR
    noise = RNG.normal(0, 1, len(t))
    noise -= np.convolve(noise, np.ones(11) / 11, mode='same')
    return (.65 * noise + .35 * np.sin(2 * np.pi * 175 * t)) * np.exp(-t * 19)


def hat(open_hat=False):
    t = np.arange(round((.22 if open_hat else .075) * SR)) / SR
    noise = RNG.normal(0, 1, len(t))
    noise -= np.convolve(noise, np.ones(5) / 5, mode='same')
    return noise * np.exp(-t * (19 if open_hat else 60))


def render():
    out = np.zeros(COUNT)
    phrases = [
        ([0, .75, 1.5, 2, 2.75, 3.5], [52, 52, 55, 57, 52, 50]),
        ([0, 1, 1.5, 2.5, 3.25], [52, 59, 57, 55, 50]),
        ([0, .75, 1.5, 2, 2.75, 3.5], [52, 52, 55, 57, 52, 50]),
        ([0, 1, 1.5, 2.5, 3.25], [55, 59, 57, 55, 52]),
        ([0, .75, 1.5, 2, 2.75, 3.5], [52, 52, 55, 57, 52, 50]),
        ([0, 1, 1.5, 2.5, 3.25], [52, 59, 57, 50, 52]),
        ([0, .75, 1.5, 2, 2.75, 3.5], [52, 52, 55, 57, 52, 50]),
        ([0, 1, 1.5, 2.5, 3.25], [52, 59, 57, 50, 52]),
    ]
    for bar, (positions, notes) in enumerate(phrases):
        start = bar * 4 * BEAT
        for index, (position, midi) in enumerate(zip(positions, notes)):
            accent = index == 0 or position == 2
            duration = .43 if accent else .26
            riff = guitar(midi, duration, not accent)
            if accent:
                riff += .60 * guitar(midi + 7, duration)
                riff += .25 * guitar(midi + 12, duration)
            add(out, riff, start + position * BEAT, .38 if accent else .32)
            # Quiet independently-excited doubled guitar, still a mono deliverable.
            add(out, guitar(midi, duration, not accent), start + position * BEAT + .009, .12)
            add(out, bass(midi - 24, duration), start + position * BEAT, .32)
        for position in [.5, 2.5] if bar % 2 == 0 else [.5, 2]:
            add(out, guitar(52, .18, True), start + position * BEAT, .18)
        for position in [0, 1.5, 2, 3.5] if bar % 2 == 0 else [0, 1.5, 2.5, 3.5]:
            add(out, kick(), start + position * BEAT, .52)
        for position in [1, 3]:
            add(out, snare(), start + position * BEAT, .30)
        for eighth in range(8):
            add(out, hat(eighth == 7 and bar % 2 == 1), start + eighth * BEAT / 2, .036 if eighth % 2 else .026)
        if bar == 7:
            add(out, snare(), start + 3.5 * BEAT, .12)
    out -= out.mean()
    out = np.tanh(out * .8)
    out *= .82 / np.max(np.abs(out)) * 10 ** (MASTER_TRIM_DB / 20)
    ramp = round(.004 * SR)
    out[:ramp] *= np.linspace(0, 1, ramp)
    out[-ramp:] *= np.linspace(1, 0, ramp)
    return out


if __name__ == '__main__':
    ROOT.mkdir(parents=True, exist_ok=True)
    signal = render()
    assert np.isfinite(signal).all()
    pcm = (np.clip(signal, -1, 1) * 32767).astype('<i2')
    assert len(pcm) == COUNT and pcm[0] == pcm[-1] == 0
    dest = ROOT / 'rock_loop_v2.wav'
    with wave.open(str(dest), 'wb') as stream:
        stream.setnchannels(1)
        stream.setsampwidth(2)
        stream.setframerate(SR)
        stream.writeframes(pcm.tobytes())
    with wave.open(str(dest), 'rb') as stream:
        assert (stream.getnchannels(), stream.getsampwidth(), stream.getframerate(), stream.getnframes()) == (1, 2, SR, COUNT)
    result = {'approval': 'preview only; pending exact audition', 'file': dest.name,
              'seconds': COUNT / SR, 'bpm': 115, 'bars': 8, 'sampleRate': SR, 'channels': 1, 'bits': 16,
              'peakDbfs': float(20 * np.log10(np.max(np.abs(pcm.astype(float))) / 32768)),
              'rmsDbfs': float(20 * np.log10(np.sqrt(np.mean((pcm.astype(float) / 32768) ** 2)))),
              'sha256': hashlib.sha256(dest.read_bytes()).hexdigest()}
    (ROOT / 'technical.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, indent=2))
