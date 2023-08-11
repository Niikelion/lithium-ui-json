using System;
using System.Linq;
using UnityEngine;
using System.Collections;
using Newtonsoft.Json.Linq;
using JetBrains.Annotations;
using UnityEngine.UIElements;
using System.Collections.Generic;

using CU = UI.Li.Utils.CompositionUtils;

namespace UI.Li.Json
{
    public static class JsonUI
    {
        #region Helpers
        //TODO: move to main package
        private class MutableList<T> : IMutableValue, IList<T>
        {
            public int Count => values.Count;
            public bool IsReadOnly => false;
            public IEnumerable<(ulong id, T value)> IndexedValues => values;

            private readonly List<(ulong id, T value)> values = new();
            private ulong nextId;
            
            public event Action OnValueChanged;

            public MutableList(IEnumerable<T> elements)
            {
                foreach (var element in elements)
                    InternalAdd(element);
            }

            public void Dispose()
            {
                values.Clear();
                nextId = 0;
                OnValueChanged = null;
            }

            public IEnumerator<T> GetEnumerator() => values.Select(item => item.value).GetEnumerator();

            public T this[int index]
            {
                get => values[index].value;
                set
                {
                    if (EqualityComparer<T>.Default.Equals(values[index].value, value))
                        return;
                    
                    values[index] = (GetNextId(), value);
                    OnValueChanged?.Invoke();
                }
            }

            public void Swap(int index1, int index2)
            {
                if (index1 < 0 || index1 >= Count)
                    throw new ArgumentOutOfRangeException(nameof(index1));
                if (index2 < 0 || index2 >= Count)
                    throw new ArgumentOutOfRangeException(nameof(index2));

                (values[index1], values[index2]) = (values[index2], values[index1]);
                OnValueChanged?.Invoke();
            }
            
            public void Add(T item)
            {
                InternalAdd(item);
                OnValueChanged?.Invoke();
            }

            public void Clear()
            {
                values.Clear();
                OnValueChanged?.Invoke();
            }

            public bool Contains(T item)
            {
                foreach (var i in values)
                    if (EqualityComparer<T>.Default.Equals(i.value, item))
                        return true;

                return false;
            }

            public void CopyTo(T[] array, int arrayIndex)
            {
                int end = Math.Min(array.Length, arrayIndex + Count);

                for (int i = arrayIndex; i < end; ++i)
                    array[i] = values[i].value;
            }

            public bool Remove(T item)
            {
                int index = IndexOf(item);
                if (index < 0)
                    return false;
                
                RemoveAt(index);
                return true;
            }

            public int IndexOf(T item)
            {
                for (int i=0; i<values.Count; ++i)
                    if (EqualityComparer<T>.Default.Equals(values[i].value, item))
                        return i;
                
                return -1;
            }

            public void Insert(int index, T item)
            {
                values.Insert(index, (GetNextId(), item));
                OnValueChanged?.Invoke();
            }

            public void RemoveAt(int index)
            {
                values.RemoveAt(index);
                OnValueChanged?.Invoke();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private ulong GetNextId()
            {
                ulong ret = nextId++;

                if (nextId == 0)
                    throw new Exception("Index overflow, not sure how but yeah");

                return ret;
            }

            private void InternalAdd(T item) => values.Add((GetNextId(), item));
        }

        private struct JsonTypeEntry
        {
            public readonly Func<JToken, Action<JToken>, IComponent> Handler;
            public readonly Func<JToken> Creator;
            public readonly string Name;
            public readonly JTokenType[] Types;

            public JsonTypeEntry(string name, Func<JToken, Action<JToken>, IComponent> handler, Func<JToken> creator, JTokenType[] types)
            {
                Name = name;
                Handler = handler;
                Creator = creator;
                Types = types;
            }
            
            public JsonTypeEntry(string name, Func<JToken, Action<JToken>, IComponent> handler, Func<JToken> creator, JTokenType type)
            {
                Name = name;
                Handler = handler;
                Creator = creator;
                Types = new[] { type };
            }
        }

        private static Dictionary<JTokenType, JsonTypeEntry> TypeMapping => typeMapping ??= CreateMapping();
        private static Dictionary<JTokenType, JsonTypeEntry> typeMapping;

        private static readonly JsonTypeEntry[] types = {
            new("Null", type: JTokenType.Null,
                creator: JValue.CreateNull,
                handler: (_, _) => Null()),
            new("Number", types: new[] { JTokenType.Float, JTokenType.Integer },
                creator: () => 0,
                handler: Number),
            new("String", type: JTokenType.String,
                creator: () => "",
                handler: String),
            new("Array", type: JTokenType.Array,
                creator: () => new JArray(),
                handler: Array),
            new("Object", type: JTokenType.Object,
                creator: () => new JObject(),
                handler: Object)
        };
        
        private static Dictionary<JTokenType, JsonTypeEntry> CreateMapping()
        {
            Dictionary<JTokenType, JsonTypeEntry> ret = new();

            foreach (var entry in types)
                foreach (var type in entry.Types)
                    ret.Add(type, entry);

            return ret;
        }
        #endregion

        [PublicAPI]
        public static IComponent Value(JToken initialValue, [NotNull] Action<JToken> onValueChanged) =>
            CU.Flex(
                data: new( flexGrow: 1 ),
                content: new[]
                {
                    initialValue == null
                        ? Null()
                        : TypeMapping[initialValue.Type].Handler(initialValue, onValueChanged)
                }
            );

        public static IComponent DynamicTypeValue([NotNull] JToken initialValue,
            [NotNull] Action<JToken> onValueChanged) =>
            new Component(ctx =>
            {
                var typeNames = types.Select(t => t.Name).ToList();
                
                var type = ctx.RememberF(() => typeNames.IndexOf(TypeMapping[initialValue.Type].Name));
                var tmpValue = ctx.RememberRef(initialValue);

                void OnValueChanged(JToken t)
                {
                    tmpValue.Value = t;
                    onValueChanged(t);
                }

                return CU.Flex(
                    direction: FlexDirection.Row,
                    data: new(
                        alignItems: Align.FlexStart,
                        justifyContent: Justify.SpaceBetween,
                        flexGrow: 1
                    ),
                    content: new[]
                    {
                        CU.Flex(direction: FlexDirection.Row, content: new IComponent[]
                        {
                            CU.Dropdown(
                                type.Value,
                                data: new(
                                    width: 70
                                ),
                                onSelectionChanged: newType =>
                                {
                                    if (type.Value == newType)
                                        return;

                                    tmpValue.Value = types[newType].Creator();

                                    using var _ = ctx.BatchOperations();

                                    type.Value = newType;
                                    OnValueChanged(tmpValue.Value);
                                },
                                options: typeNames
                            ),
                            CU.Text(":")
                        }),
                        CU.WithId(type + 1, Value(tmpValue.Value, OnValueChanged))
                    });
            }, isStatic: true);

        private static IComponent Null() => CU.Text("Null");

        private static IComponent String([NotNull] JToken initialValue, [NotNull] Action<JToken> onValueChanged) =>
            new Component(ctx =>
            {
                var editing = ctx.Remember(false);
                var currentValue = ctx.Remember(initialValue.Value<string>());

                var tmpValue = ctx.RememberRef(initialValue.ToString());

                void StartEditing() => editing.Value = true;

                void FinishEditing()
                {
                    using var _ = ctx.BatchOperations();
                    
                    editing.Value = false;

                    if (currentValue.Value == tmpValue.Value) return;
                    
                    currentValue.Value = tmpValue.Value;
                    onValueChanged(currentValue.Value);
                }
                
                IComponent NotEditing() =>
                    CU.Flex(
                        direction: FlexDirection.Row,
                        content: new[] { CU.Text("\""), CU.Text(currentValue), CU.Text("\"") },
                        data: new(
                            onClick: StartEditing
                        ));

                IComponent Editing() =>
                    CU.TextField(
                        v => tmpValue.Value = v,
                        currentValue,
                        data: new(
                            minWidth: 200,
                            onBlur: FinishEditing,
                            onKeyDown: e =>
                            {
                                if (e.Character == '\n')
                                    FinishEditing();
                            }
                        ));
                
                return CU.Switch(editing, Editing, NotEditing);
            });

        private static IComponent Number([NotNull] JToken initialValue, [NotNull] Action<JToken> onValueChanged) =>
            new Component(ctx =>
            {
                var editing = ctx.Remember(false);
                var currentValue = ctx.Remember(initialValue.Value<float>());
                var tmpValue = ctx.RememberRef(initialValue.ToString());

                string ValueAsString() => currentValue.Value.ToString("0");
                
                void StartEditing() => editing.Value = true;

                void FinishEditing()
                {
                    using var _ = ctx.BatchOperations();
                    
                    editing.Value = false;

                    if (!float.TryParse(tmpValue.Value, out float value))
                        return;

                    if (Mathf.Approximately(currentValue.Value, value)) return;
                    
                    currentValue.Value = value;
                    onValueChanged(currentValue.Value);
                }

                IComponent NotEditing() =>
                    CU.Text(ValueAsString(), data: new(
                        onClick: StartEditing
                    ));

                IComponent Editing() =>
                    CU.TextField(
                        v => tmpValue.Value = v,
                        ValueAsString(),
                        data: new(
                            minWidth: 200,
                            onBlur: FinishEditing,
                            onKeyDown: e =>
                            {
                                if (e.Character == '\n')
                                    FinishEditing();
                            }
                        ));
                
                return CU.Switch(editing, Editing, NotEditing);
            });

        private static IComponent Array([NotNull] JToken initialValue, [NotNull] Action<JToken> onValueChanged) =>
            new Component(ctx =>
            {
                var items = ctx.Use(() => new MutableList<JToken>(initialValue.Children()));

                void InvokeOnValueChanged()
                {
                    var r = new JArray();

                    foreach (var i in items)
                        r.Add(i);
                            
                    onValueChanged(r);
                }
                
                void AddElement()
                {
                    using var _ = ctx.BatchOperations();
                    
                    items.Add(JValue.CreateNull());
                    InvokeOnValueChanged();
                }

                void RemoveElement(int index)
                {
                    using var _ = ctx.BatchOperations();
                    
                    items.RemoveAt(index);
                    InvokeOnValueChanged();
                }

                static IComponent ArrayItem(
                    int index,
                    [NotNull] JToken initialValue,
                    [NotNull] Action<JToken> onValueChanged,
                    [NotNull] Action onRequestRemove,
                    [NotNull] Action onRequestMoveUp,
                    [NotNull] Action onRequestMoveDown
                ) => CU.Flex(
                    direction: FlexDirection.Row,
                    data: new(alignItems: Align.FlexStart),
                    content: new[]
                    {
                        CU.Text($"{index}:", data: new ( minWidth: 20 )),
                        CU.Button(onRequestRemove, "-"),
                        CU.Button(onRequestMoveUp, "^"),
                        CU.Button(onRequestMoveDown, "v"),
                        DynamicTypeValue(initialValue, onValueChanged)
                    });
                
                IComponent AddButton() =>
                    CU.Button(AddElement, "Add");

                var content = new List<IComponent>();
                
                content.AddRange(items.IndexedValues.Select((item, index) =>
                {
                    (ulong id, var token) = item;

                    return CU.WithId((int)id + 2, ArrayItem(
                        index: index,
                        initialValue: token,
                        onValueChanged: value =>
                        {
                            using var _ = ctx.BatchOperations();
                        
                            items[index] = value;
                            InvokeOnValueChanged();
                        },
                        onRequestRemove: () => RemoveElement(index),
                        onRequestMoveUp: () =>
                        {
                            if (index == 0)
                                return;
                            
                            using var _ = ctx.BatchOperations();

                            items.Swap(index, index - 1);
                            InvokeOnValueChanged();
                        },
                        onRequestMoveDown: () =>
                        {
                            if (index + 1 == items.Count)
                                return;
                            
                            using var _ = ctx.BatchOperations();

                            items.Swap(index, index + 1);
                            InvokeOnValueChanged();
                        }
                    ));
                }));
                
                content.Add(CU.WithId(1, AddButton()));
                
                return CU.Flex(content);
            }, isStatic: true);

        private static IComponent Object([NotNull] JToken initialValue, [NotNull] Action<JToken> onValueChanged) =>
            new Component(ctx =>
            {
                var obj = (JObject)initialValue;
                
                var items = ctx.Use(() => new MutableList<(string name, JToken value)>(obj.Properties().Select(p => (p.Name, p.Value))));

                void InvokeOnValueChanged()
                {
                    var r = new JObject();

                    foreach (var i in items)
                        r.Add(i.name, i.value);
                            
                    onValueChanged(r);
                }
                
                void AddElement(string name)
                {
                    using var _ = ctx.BatchOperations();
                    
                    items.Add((name, JValue.CreateNull()));
                    InvokeOnValueChanged();
                }

                static IComponent ObjectItem(
                    string name,
                    [NotNull] JToken initialValue,
                    [NotNull] Action<string> onNameChanged,
                    [NotNull] Action<JToken> onValueChanged,
                    [NotNull] Action onRequestRemove,
                    [NotNull] Action onRequestMoveUp,
                    [NotNull] Action onRequestMoveDown
                ) => CU.Flex(
                    direction: FlexDirection.Row,
                    data: new ( alignItems: Align.FlexStart ),
                    content: new[]
                {
                    CU.Button(onRequestRemove, "-"),
                    CU.Button(onRequestMoveUp, "^"),
                    CU.Button(onRequestMoveDown, "v"),
                    String(name, v => onNameChanged(v.Value<string>())),
                    DynamicTypeValue(initialValue, onValueChanged)
                });
                
                IComponent AddField() =>
                    new Component(fCtx =>
                    {
                        var tmpValue = fCtx.RememberRef("");

                        return CU.TextField(
                            v => tmpValue.Value = v,
                            data: new(
                                onKeyDown: e =>
                                {
                                    using var _ = fCtx.BatchOperations();

                                    if (e.Character != '\n') return;
                                    
                                    AddElement(tmpValue.Value);
                                    tmpValue.NotifyChanged();
                                }
                            )
                        );
                    });
                
                var content = new List<IComponent>();

                content.AddRange(items.IndexedValues.Select((item, index) =>
                {
                    (ulong id, var tokenItem) = item;

                    (string name, var token) = tokenItem;
                    
                    return CU.WithId((int)id + 2, ObjectItem(
                        name: name,
                        initialValue: token,
                        onNameChanged: n =>
                        {
                            using var _ = ctx.BatchOperations();

                            items[index] = (n, items[index].value);
                            InvokeOnValueChanged();
                        },
                        onValueChanged: value =>
                        {
                            using var _ = ctx.BatchOperations();

                            items[index] = (items[index].name, value);
                            InvokeOnValueChanged();
                        },
                        onRequestRemove: () =>
                        {
                            using var _ = ctx.BatchOperations();

                            items.RemoveAt(index);
                            InvokeOnValueChanged();
                        },
                        onRequestMoveDown: () =>
                        {
                            if (index == 0)
                                return;
                            
                            using var _ = ctx.BatchOperations();

                            items.Swap(index, index - 1);
                            InvokeOnValueChanged();
                        },
                        onRequestMoveUp: () =>
                        {
                            if (index + 1 == items.Count)
                                return;
                            
                            using var _ = ctx.BatchOperations();

                            items.Swap(index, index + 1);
                            InvokeOnValueChanged();
                        }
                    ));
                }));
                
                content.Add(CU.WithId(1, AddField()));
                
                return CU.Flex(content);
            });
    }
}