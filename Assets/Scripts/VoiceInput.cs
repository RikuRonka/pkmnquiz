#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Windows.Speech;

public class VoiceInput : MonoBehaviour
{
    public Action<string> OnHeard;
    public bool IsRunning => recognizer != null && recognizer.IsRunning;

    private KeywordRecognizer recognizer;

    public void StartListening(IEnumerable<string> phrases)
    {
        StopListening();

        var list = new List<string>();
        foreach (var p in phrases)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            list.Add(p);                    // keep original casing; engine handles it
        }
        if (list.Count == 0) { Debug.LogWarning("[Voice] No phrases."); return; }

        recognizer = new KeywordRecognizer(list.ToArray(), ConfidenceLevel.Medium);
        recognizer.OnPhraseRecognized += OnPhraseRecognized;
        recognizer.Start();
        Debug.Log($"[Voice] Started with {list.Count} phrases.");
    }

    public void StopListening()
    {
        if (recognizer != null)
        {
            try { if (recognizer.IsRunning) recognizer.Stop(); } catch { }
            recognizer.OnPhraseRecognized -= OnPhraseRecognized;
            recognizer.Dispose();
            recognizer = null;
            Debug.Log("[Voice] Stopped.");
        }
    }

    private void OnPhraseRecognized(PhraseRecognizedEventArgs args)
    {
        Debug.Log($"[Voice] Heard='{args.text}' conf={args.confidence}");

        OnHeard?.Invoke(args.text);
    }

    private void OnDestroy() => StopListening();

}
#endif
