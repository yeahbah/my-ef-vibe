using System.Collections.Generic;
using MyEfVibe.VisualStudio.Models;

namespace MyEfVibe.VisualStudio.Services;

internal sealed class EfvibeSessionState
{
    private readonly List<HistoryEntry> _history = new();
    private readonly object _gate = new();

    internal IReadOnlyList<HistoryEntry> History
    {
        get
        {
            lock (_gate)
                return _history.ToArray();
        }
    }

    internal void RecordEvaluation(string expression, EvaluationJsonPayload payload)
    {
        lock (_gate)
        {
            _history.Insert(0, new HistoryEntry(expression, payload));

            if (_history.Count > 50)
                _history.RemoveAt(_history.Count - 1);
        }
    }

    internal sealed class HistoryEntry
    {
        public HistoryEntry(string expression, EvaluationJsonPayload payload)
        {
            Expression = expression;
            Payload = payload;
        }

        public string Expression { get; }
        public EvaluationJsonPayload Payload { get; }
    }
}
