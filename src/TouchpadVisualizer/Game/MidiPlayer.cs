using System.Runtime.InteropServices;
using System.Diagnostics;

namespace TouchpadVisualizer.Game;

/// <summary>
/// Plays piano notes using the Windows built-in MIDI synthesizer.
/// Uses P/Invoke to winmm.dll — zero external dependencies required.
/// </summary>
public class MidiPlayer : IDisposable
{
    [DllImport("winmm.dll")]
    private static extern int midiOutOpen(out IntPtr handle, int deviceId, IntPtr callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    private static extern int midiOutShortMsg(IntPtr handle, int message);

    [DllImport("winmm.dll")]
    private static extern int midiOutClose(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int midiOutReset(IntPtr handle);

    private IntPtr _handle;
    private bool _isOpen;
    private bool _disposed;
    private readonly object _lock = new();

    // Track active notes for proper cleanup
    private readonly HashSet<int> _activeNotes = new();

    /// <summary>
    /// Opens the default MIDI output device and sets it to Acoustic Grand Piano.
    /// </summary>
    public bool Open()
    {
        // deviceId = -1 means MIDI_MAPPER (default device)
        int result = midiOutOpen(out _handle, -1, IntPtr.Zero, IntPtr.Zero, 0);
        _isOpen = result == 0;

        if (_isOpen)
        {
            // Set instrument to Acoustic Grand Piano (Program 0) on channel 0
            SetInstrument(0, 0);
            Debug.WriteLine("[MidiPlayer] Opened successfully.");
        }
        else
        {
            Debug.WriteLine($"[MidiPlayer] Failed to open MIDI device. Error code: {result}");
        }

        return _isOpen;
    }

    /// <summary>
    /// Changes the instrument (program) on a given MIDI channel.
    /// </summary>
    public void SetInstrument(int channel, int program)
    {
        if (!_isOpen) return;
        lock (_lock)
        {
            // Program Change message: 0xCn pp (n=channel, pp=program)
            int msg = 0xC0 | (channel & 0x0F) | ((program & 0x7F) << 8);
            midiOutShortMsg(_handle, msg);
        }
    }

    /// <summary>
    /// Sends a Note On message. The note will sustain until NoteOff is called.
    /// </summary>
    public void NoteOn(byte note, byte velocity = 100, byte channel = 0)
    {
        if (!_isOpen) return;
        lock (_lock)
        {
            // Note On: 0x9n nn vv
            int msg = 0x90 | (channel & 0x0F) | ((note & 0x7F) << 8) | ((velocity & 0x7F) << 16);
            midiOutShortMsg(_handle, msg);
            _activeNotes.Add(note | (channel << 8));
        }
    }

    /// <summary>
    /// Sends a Note Off message.
    /// </summary>
    public void NoteOff(byte note, byte channel = 0)
    {
        if (!_isOpen) return;
        lock (_lock)
        {
            // Note Off: 0x8n nn vv
            int msg = 0x80 | (channel & 0x0F) | ((note & 0x7F) << 8);
            midiOutShortMsg(_handle, msg);
            _activeNotes.Remove(note | (channel << 8));
        }
    }

    /// <summary>
    /// Plays a note for a specified duration (fire-and-forget).
    /// </summary>
    public async void PlayNote(byte note, byte velocity = 100, int durationMs = 300, byte channel = 0)
    {
        NoteOn(note, velocity, channel);
        await Task.Delay(durationMs);
        NoteOff(note, channel);
    }

    /// <summary>
    /// Silences all currently sounding notes.
    /// </summary>
    public void AllNotesOff()
    {
        if (!_isOpen) return;
        lock (_lock)
        {
            midiOutReset(_handle);
            _activeNotes.Clear();
        }
    }

    /// <summary>
    /// Plays a short error/buzzer sound for wrong taps.
    /// </summary>
    public void PlayErrorSound()
    {
        if (!_isOpen) return;
        // Play a dissonant low cluster briefly
        PlayNote(36, 60, 150, 0);  // C2
        PlayNote(37, 60, 150, 0);  // C#2
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_isOpen)
        {
            lock (_lock)
            {
                midiOutReset(_handle);
                midiOutClose(_handle);
                _isOpen = false;
            }
        }
    }
}
