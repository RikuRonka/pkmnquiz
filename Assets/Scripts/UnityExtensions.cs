using UnityEngine;

public static class UnityExtensions
{
    public static T GetOrAdd<T>(this Component c) where T : Component
    {
        var existing = c.GetComponent<T>();
        return existing ? existing : c.gameObject.AddComponent<T>();
    }

    public static T GetOrAdd<T>(this GameObject go) where T : Component
    {
        var existing = go.GetComponent<T>();
        return existing ? existing : go.AddComponent<T>();
    }

    public static RectTransform EnsureChildRect(this Transform parent, string name)
    {
        var t = parent.Find(name);
        if (!t)
        {
            var go = new GameObject(name, typeof(RectTransform));
            t = go.transform;
            t.SetParent(parent, false);
        }
        return (RectTransform)t;
    }
}