"""Original sample-free instrumental placeholders, not imitations of any artist.

Run with Python + numpy. Writes only sibling WAVs and technical.json.
Indie: clean jangly guitars, melodic bass, relaxed live-kit backbeat.
Metal: low distorted power chords, palm-muted gallops, double-kick passages.
"""
from pathlib import Path
import hashlib
import json
import wave
import numpy as np

ROOT = Path(__file__).resolve().parent
SR = 22050


def render(genre, bpm):
    rng = np.random.default_rng(20261002 if genre == 'indie' else 20261003)
    beat = 60 / bpm
    count = round(16 * 4 * beat * SR)
    out = np.zeros((count, 2))

    def add(sound, at, gain, pan=0):
        indices = (round(at * SR) + np.arange(len(sound))) % count
        for channel, level in enumerate([np.sqrt((1-pan)/2), np.sqrt((1+pan)/2)]):
            np.add.at(out[:, channel], indices, sound * gain * level)

    def times(duration):
        return np.arange(round(duration * SR)) / SR

    def env(t, duration, attack=.004, release=.035):
        return np.minimum(1, t / attack) * np.minimum(1, np.maximum(0, duration-t) / release)

    def guitar(midi, duration, muted=False):
        t = times(duration)
        f = 440 * 2 ** ((midi-69)/12)
        if genre == 'indie':
            # Plucked string with slightly detuned, quickly decaying upper partials.
            body = sum(np.sin(2*np.pi*f*h*t + .13*h) / h**1.25 * np.exp(-t*(1.3+h*.6)) for h in range(1, 9))
            return body * env(t, duration) * .6
        body = sum(np.sin(2*np.pi*f*h*t) / h**1.1 for h in range(1, 7))
        body = np.tanh(3.2*body)
        body = np.convolve(body, np.ones(7)/7, mode='same')
        return body * np.exp(-t*(17 if muted else 1.8)) * env(t, duration)

    def bass(midi, duration):
        t = times(duration)
        p = 2*np.pi*440*2**((midi-69)/12)*t
        return (np.sin(p)+.2*np.sin(2*p)) * np.exp(-t*2) * env(t, duration)

    def drum(kind):
        t = times(.28 if kind != 'hat' else .09)
        if kind == 'kick':
            p = 2*np.pi*(43*t+95*(1-np.exp(-t*35))/35)
            return (np.sin(p)+rng.normal(0,.15,len(t))*np.exp(-t*150))*np.exp(-t*19)
        noise = rng.normal(0, 1, len(t))
        noise -= np.convolve(noise, np.ones(7)/7, mode='same')
        if kind == 'hat':
            return noise * np.exp(-t*55)
        return (noise*.6 + np.sin(2*np.pi*185*t)*.25)*np.exp(-t*21)

    for bar in range(16):
        start = bar*4*beat
        if genre == 'indie':
            chord = [[55,59,62,66],[50,57,62,66],[52,55,59,62],[48,55,60,64]][bar % 4]
            # Two independently strummed sides; upper picked melody changes in second half.
            for position in [0, .75, 1.5, 2.5, 3.5]:
                for side in [-.65,.65]:
                    for n, note in enumerate(chord):
                        add(guitar(note,.65), start+position*beat+n*.011+(side>0)*.008,.12,side)
            melody = [67,71,74,71,66,69,74,69,67,71,76,74,67,64,62,64]
            for n in range(4):
                note = melody[(bar % 4)*4+n] + (12 if bar >= 12 and n == 2 else 0)
                tone = guitar(note,.38)
                add(tone,start+(n+.25)*beat,.22,.18)
                add(tone,start+(n+.25)*beat+.17,.035,-.3)
            root = [31,26,28,24][bar%4]
            for n, interval in enumerate([0,0,7,12,0,7,0,7]):
                add(bass(root+interval,.23),start+n*.5*beat,.28)
            kicks = [0,1.5,2,2.75] if bar%2 == 0 else [0,2,3.5]
        else:
            root = [40,40,36,38][bar%4]
            steps = [0,.5,.75,1,1.5,1.75,2,2.5,2.75,3,3.5,3.75]
            for n, position in enumerate(steps):
                accent = position in [0,2,3]
                note = root + (3 if bar%4 == 3 and position >= 3 else 0)
                duration = .28 if accent else .115
                for pan in [-.72,.72]:
                    riff = guitar(note,duration,not accent) + .58*guitar(note+7,duration,not accent)
                    add(riff,start+position*beat+(pan>0)*.006,.23,pan)
                add(bass(note-12,duration),start+position*beat,.27)
            if bar >= 8:
                for n, interval in enumerate([12,15,14,10]):
                    add(guitar(root+interval,.22),start+(n+.25)*beat,.10,.15)
            kicks = [0,.5,.75,1.5,2,2.5,2.75,3.5] if bar%2 == 0 else [0,.25,.5,1.5,2,2.25,2.5,3.5]
        for position in kicks:
            add(drum('kick'),start+position*beat,.50)
        for position in [1,3]:
            add(drum('snare'),start+position*beat,.27)
        for n in range(8):
            add(drum('hat'),start+n*.5*beat,.034 if n%2 else .046,.35)
        if bar in [7,15]:
            for n in range(3):
                add(drum('snare'),start+(3.25+n*.25)*beat,.12+n*.025,-.2)
    # Small circular room tail; crowd ambience remains a separate in-game layer.
    dry = out.copy()
    for seconds, gain in [(.043,.05),(.083,.032),(.127,.018)]:
        out += np.roll(dry,round(seconds*SR),axis=0)*gain
    out -= out.mean(axis=0)
    out = np.tanh(out*.85)
    out *= .105 / np.sqrt(np.mean(out*out))
    if np.max(np.abs(out)) > .86:
        out *= .86 / np.max(np.abs(out))
    # Measured EBU R128 trims, matching the existing prototype loops near -18.5 LUFS.
    out *= 10 ** ((.33 if genre == 'indie' else 1.15) / 20)
    ramp = round(.004*SR)
    out[:ramp] *= np.linspace(0,1,ramp)[:,None]
    out[-ramp:] *= np.linspace(1,0,ramp)[:,None]
    return out


if __name__ == '__main__':
    results = []
    for genre, bpm in [('indie',112),('metal',148)]:
        signal = render(genre,bpm)
        assert np.isfinite(signal).all()
        pcm = np.round(signal*32767).astype('<i2')
        assert (pcm[0] == 0).all() and (pcm[-1] == 0).all()
        path = ROOT / (genre+'_loop_v1.wav')
        with wave.open(str(path),'wb') as stream:
            stream.setnchannels(2)
            stream.setsampwidth(2)
            stream.setframerate(SR)
            stream.writeframes(pcm.tobytes())
        with wave.open(str(path),'rb') as stream:
            assert stream.getnframes() == len(pcm) and stream.getnchannels() == 2
        results.append(dict(file=path.name,bpm=bpm,bars=16,seconds=len(pcm)/SR,
            sampleRate=SR,channels=2,bits=16,peakDbfs=float(20*np.log10(np.max(np.abs(signal)))),
            rmsDbfs=float(20*np.log10(np.sqrt(np.mean(signal*signal)))),
            sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    (ROOT/'technical.json').write_text(json.dumps(dict(provenance='Original procedural synthesis; no external samples or artist imitation; temporary live-set instrumentals.',files=results),indent=2)+'\n',encoding='utf-8')
    print(json.dumps(results,indent=2))
