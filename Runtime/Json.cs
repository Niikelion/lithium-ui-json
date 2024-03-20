using System;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json.Linq;
using JetBrains.Annotations;
using UnityEngine.UIElements;
using System.Collections.Generic;
using UI.Li.Common;
using UI.Li.Utils;
using UI.Li.Utils.Continuations;
using CU = UI.Li.Utils.CompositionUtils;

namespace UI.Li.Json
{
    public static class JsonUI
    {
        #region Helpers
        private struct JsonTypeEntry
        {
            //TODO: after portals are implemented in the main package, change it back to IComponent and use them instead
            public readonly Func<JToken, Action<JToken>, (IComponent, IComponent)> Handler;
            public readonly Func<JToken> Creator;
            public readonly string Name;
            public readonly JTokenType[] Types;

            public JsonTypeEntry(string name, Func<JToken, Action<JToken>, (IComponent, IComponent)> handler, Func<JToken> creator, JTokenType[] types)
            {
                Name = name;
                Handler = handler;
                Creator = creator;
                Types = types;
            }
            
            public JsonTypeEntry(string name, Func<JToken, Action<JToken>, (IComponent, IComponent)> handler, Func<JToken> creator, JTokenType type)
            {
                Name = name;
                Handler = handler;
                Creator = creator;
                Types = new[] { type };
            }
        }

        private static Dictionary<JTokenType, JsonTypeEntry> TypeMapping => typeMapping ??= CreateMapping();
        private static Dictionary<JTokenType, JsonTypeEntry> typeMapping;

        private static readonly Style fillStyle = new (flexGrow: 1);
        
        private static readonly JsonTypeEntry[] types = {
            new("Null", type: JTokenType.Null,
                creator: JValue.CreateNull,
                handler: (_, _) => (Null(), null)),
            new("Number", types: new[] { JTokenType.Float, JTokenType.Integer },
                creator: () => 0,
                handler: (v, c) => (Number(v, c), null)),
            new("String", type: JTokenType.String,
                creator: () => "",
                handler: (v, c) => (String(v, c), null)),
            new("Array", type: JTokenType.Array,
                creator: () => new JArray(),
                handler: (v, c) => (CU.Text("["), CU.Flex(content: IComponent.Seq(
                        Array(v, c),
                        CU.Text("]")
                    ), direction: FlexDirection.Column))),
            new("Object", type: JTokenType.Object,
                creator: () => new JObject(),
                handler: (v, c) => (CU.Text("{"), CU.Flex(content: IComponent.Seq(
                        Object(v, c),
                        CU.Text("}")
                    ), direction: FlexDirection.Column)))
        };
        
        private static Dictionary<JTokenType, JsonTypeEntry> CreateMapping()
        {
            Dictionary<JTokenType, JsonTypeEntry> ret = new();

            foreach (var entry in types)
                foreach (var type in entry.Types)
                    ret.Add(type, entry);

            return ret;
        }

        private static JsonTypeEntry GetEntryForValue(JToken value) => TypeMapping[value?.Type ?? JTokenType.Null];
        #endregion

        [PublicAPI]
        public static (IComponent, IComponent) Value(JToken initialValue, [NotNull] Action<JToken> onValueChanged)
        {
            var (first, second) = GetEntryForValue(initialValue).Handler(initialValue, onValueChanged);
            
            return (
                first?.Let(f => CU.Box(f).WithStyle(fillStyle)),
                second?.Let(s => CU.Box(s).WithStyle(fillStyle))
            );
        }

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

                var (first, second) = Value(tmpValue.Value, OnValueChanged);
                
                return CU.Flex(
                    direction: FlexDirection.Row,
                    content: IComponent.Seq(
                        CU.Flex(direction: FlexDirection.Row, content: IComponent.Seq(
                            CU.Dropdown(
                                type.Value,
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
                            ).WithStyle(new (width: 70)),
                            CU.Text(":")
                        ).Let(s => first != null ? s.Append(CU.WithId(type + 1, first)) : s))
                    ).Let(s => second != null ? s.Append(CU.WithId(type + 1, second)) : s)
                ).WithStyle(new (
                    alignItems: Align.FlexStart,
                    justifyContent: Justify.SpaceBetween,
                    flexGrow: 1
                    ));
            }, isStatic: true);

        private static IComponent Null() => CU.Text("Null");

        private static IComponent String([NotNull] JToken initialValue, [NotNull] Action<JToken> onValueChanged) =>
            new Component(ctx =>
            {
                var editing = ctx.Remember(false);
                var currentValue = ctx.Remember(initialValue.Value<string>());

                var tmpValue = ctx.RememberRef(initialValue.ToString());

                return CU.Switch(editing, Editing, NotEditing);

                void StartEditing() => editing.Value = true;

                void FinishEditing()
                {
                    using var _ = ctx.BatchOperations();
                    
                    editing.Value = false;

                    if (currentValue.Value == tmpValue.Value) return;
                    
                    currentValue.Value = tmpValue.Value;
                    onValueChanged(currentValue.Value);
                }

                IComponent Editing() =>
                    CU.TextField(
                        v => tmpValue.Value = v,
                        currentValue,
                        manipulators: new IManipulator[] {
                            new Blurrable(FinishEditing),
                            new KeyHandler(onKeyDown: e =>
                            {
                                if (e.Character == '\n')
                                    FinishEditing();
                            })
                        }).WithStyle(new (minWidth: 200));

                IComponent NotEditing() =>
                    CU.Flex(
                        direction: FlexDirection.Row,
                        content: IComponent.Seq(CU.Text("\""), CU.Text(currentValue), CU.Text("\"")),
                        manipulators: new Clickable(StartEditing)
                    );
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
                    CU.Text(ValueAsString(), manipulators: new Clickable(StartEditing));

                IComponent Editing() =>
                    CU.TextField(
                        v => tmpValue.Value = v,
                        ValueAsString(),
                        manipulators: new IManipulator[]
                        {
                            new Blurrable(FinishEditing),
                            new KeyHandler(onKeyDown: e =>
                            {
                                if (e.Character == '\n')
                                    FinishEditing();
                            })
                        }).WithStyle(new (minWidth: 200));
                
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
                    content: IComponent.Seq(
                        CU.Text($"{index}:").WithStyle(new (minWidth: 20)),
                        CU.Button(onRequestRemove, "-"),
                        CU.Button(onRequestMoveUp, "^"),
                        CU.Button(onRequestMoveDown, "v"),
                        DynamicTypeValue(initialValue, onValueChanged)
                    )).WithStyle(new (alignItems: Align.FlexStart));
                
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
                    content: IComponent.Seq(
                        CU.Button(onRequestRemove, "-"),
                        CU.Button(onRequestMoveUp, "^"),
                        CU.Button(onRequestMoveDown, "v"),
                        String(name, v => onNameChanged(v.Value<string>())),
                        DynamicTypeValue(initialValue, onValueChanged)
                )).WithStyle(new (alignItems: Align.FlexStart));
                
                IComponent AddField() =>
                    new Component(fCtx =>
                    {
                        var tmpValue = fCtx.RememberRef("");

                        return CU.TextField(
                            v => tmpValue.Value = v,
                            manipulators: new KeyHandler(onKeyDown: e =>
                            {
                                using var _ = fCtx.BatchOperations();

                                if (e.Character != '\n') return;
                                    
                                AddElement(tmpValue.Value);
                                tmpValue.NotifyChanged();
                            })
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