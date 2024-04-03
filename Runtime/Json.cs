using System;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json.Linq;
using JetBrains.Annotations;
using UnityEngine.UIElements;
using System.Collections.Generic;
using UI.Li.Common;
using UI.Li.Utils;
using CU = UI.Li.Utils.CompositionUtils;

using static UI.Li.Common.Layout.Layout;
using static UI.Li.Common.Common;

namespace UI.Li.Json
{
    [PublicAPI] public static class JsonUI
    {
        public delegate IComponent HandlerDelegate([NotNull] JToken value, [NotNull] Action<JToken> setValue, IComponent prefix = null, IComponent suffix = null);
        
        #region Helpers
        private struct JsonTypeEntry
        {
            public readonly HandlerDelegate Handler;
            public readonly Func<JToken> Creator;
            public readonly string Name;
            public readonly JTokenType[] Types;

            public JsonTypeEntry(string name, HandlerDelegate handler, Func<JToken> creator, JTokenType[] types)
            {
                Name = name;
                Handler = handler;
                Creator = creator;
                Types = types;
            }
            
            public JsonTypeEntry(string name, HandlerDelegate handler, Func<JToken> creator, JTokenType type)
            {
                Name = name;
                Handler = handler;
                Creator = creator;
                Types = new[] { type };
            }
        }

        private static Dictionary<JTokenType, JsonTypeEntry> TypeMapping => typeMapping ??= CreateMapping();
        private static Dictionary<JTokenType, JsonTypeEntry> typeMapping;

        #region styles
        private static readonly Style fillStyle = new (flexGrow: 1);
        private static readonly Style scopeStyle = new(
            padding: new(left: 4), borderLeftWidth: 1,
            borderLeftColor: new Color(0.27f, 0.27f, 0.27f)
        );
        private static readonly Style numberTextStyle = new(
            color: new Color(0.42f, 0.58f, 0.92f)
        );
        private static readonly Style stringTextStyle = new(
            color: new Color(0.9f, 0.58f, 0.42f),
            padding: 0
        );
        private static readonly Style scopeBracketsStyle = new(
            color: new Color(0.76f, 0.57f, 1f)
        );
        #endregion
        
        private static readonly JsonTypeEntry[] types = {
            new("Null", type: JTokenType.Null,
                creator: JValue.CreateNull,
                handler: Null),
            new("String", type: JTokenType.String,
                creator: () => "",
                handler: String),
            new("Number", types: new[] { JTokenType.Float, JTokenType.Integer },
                creator: () => 0,
                handler: Number),
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

        private static JsonTypeEntry GetEntryForValue(JToken value) => TypeMapping[value?.Type ?? JTokenType.Null];
        #endregion

        public static IComponent Value([NotNull] JToken initialValue, [NotNull] Action<JToken> onValueChanged, IComponent picker, IComponent suffix)
        {
            var content = GetEntryForValue(initialValue).Handler(initialValue, onValueChanged, picker, suffix);

            return Box(content).WithStyle(fillStyle);
        }

        public static IComponent DynamicTypeValue([NotNull] JToken initialValue,
            [NotNull] Action<JToken> onValueChanged, IComponent suffix = null) =>
            new Component(ctx =>
            {
                var typeNames = types.Select(t => t.Name).ToList();
                
                var type = ctx.RememberF(() => typeNames.IndexOf(TypeMapping[initialValue.Type].Name));
                var tmpValue = ctx.RememberRef(initialValue);

                var picker = CU.Dropdown(
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
                ).WithStyle(new (margin: new (right: 4)));

                return Box(Value(tmpValue.Value, OnValueChanged, picker, suffix).Id(type.Value + 1))
                    .WithStyle(new(
                        alignItems: Align.Stretch,
                        justifyContent: Justify.SpaceBetween,
                        flexGrow: 1
                    ));

                void OnValueChanged(JToken t)
                {
                    tmpValue.Value = t;
                    onValueChanged(t);
                }
            }, isStatic: true);

        private static IComponent Null([NotNull] JToken value, [NotNull] Action<JToken> setValue, IComponent prefix = null, IComponent suffix = null) =>
            Row(
                CU.Switch(prefix != null, () => prefix, () => null),
                Text("Null").WithStyle(fillStyle),
                CU.Switch(suffix != null, () => suffix, () => null)
            ).WithStyle(new (alignItems: Align.Center));

        private static IComponent String([NotNull] JToken initialValue, [NotNull] Action<JToken> onValueChanged, IComponent prefix = null, IComponent suffix = null) =>
            new Component(ctx =>
            {
                var editing = ctx.Remember(false);
                var currentValue = ctx.Remember(initialValue.Value<string>());

                var tmpValue = ctx.RememberRef(initialValue.ToString());

                return Row(
                    CU.Switch(prefix != null, () => prefix, () => null),
                    Content().WithStyle(fillStyle),
                    CU.Switch(suffix != null, () => suffix, () => null)
                );
                
                IComponent Content() => CU.Switch(editing, Editing, NotEditing);
                
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
                    Box(
                    CU.TextField(
                        v => tmpValue.Value = v,
                        (string)currentValue ?? "",
                        focused: true,
                        manipulators: new IManipulator[]
                        {
                            new Blurrable(FinishEditing),
                            new KeyHandler(onKeyDown: e =>
                            {
                                if (e.Character == '\n')
                                    FinishEditing();
                            })
                        }).WithStyle(new(minWidth: 32)));

                IComponent NotEditing() =>
                    Row(
                        content: IComponent.Seq(
                            Text("\""),
                            Text(currentValue),
                            Text("\"")
                        ).Select(c => c.WithStyle(stringTextStyle)),
                        manipulators: new Clickable(StartEditing)
                    );
            });

        private static IComponent Number([NotNull] JToken initialValue, [NotNull] Action<JToken> onValueChanged,IComponent prefix = null, IComponent suffix = null) =>
            new Component(ctx =>
            {
                var editing = ctx.Remember(false);
                var currentValue = ctx.Remember(initialValue.Value<float>());
                var tmpValue = ctx.RememberRef(initialValue.ToString());

                return Row(
                    CU.Switch(prefix != null, () => prefix, () => null),
                    Content().WithStyle(fillStyle),
                    CU.Switch(suffix != null, () => suffix, () => null)
                );

                IComponent Content() => CU.Switch(editing, Editing, NotEditing);
                
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
                    Text(ValueAsString(), manipulators: new Clickable(StartEditing)).WithStyle(numberTextStyle);

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
            });

        private static IComponent Array([NotNull] JToken initialValue, [NotNull] Action<JToken> onValueChanged, IComponent prefix = null, IComponent suffix = null) =>
            new Component(ctx =>
            {
                var items = ctx.Use(() => new MutableList<JToken>(initialValue.Children()));

                return Col(
                    Start(),
                    Content(),
                    End()
                );

                IComponent Start() => Row(
                    CU.Switch(prefix != null, () => prefix, () => null),
                    Text("[").WithStyle(fillStyle).WithStyle(scopeBracketsStyle),
                    CU.Switch(suffix != null, () => suffix, () => null)
                );
                IComponent End() => Row(
                    Text("]").WithStyle(scopeBracketsStyle),
                    AddButton()
                ).WithStyle(new (alignItems: Align.Center));
                
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
                ) =>
                    Row(
                        Text($"{index}:").WithStyle(new(minWidth: 20)),
                        DynamicTypeValue(initialValue, onValueChanged,
                            Row(
                                Button(onRequestRemove, "x"),
                                Button(onRequestMoveUp, "^"),
                                Button(onRequestMoveDown, "v")
                            )
                        )
                    ).WithStyle(new(alignItems: Align.FlexStart));

                IComponent AddButton() => Button(AddElement, "+");

                IComponent Content()
                {
                    var content = items.IndexedValues.Select((item, index) =>
                    {
                        var (id, token) = item;

                        return ArrayItem(
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
                        ).Id((int)id + 1);
                    });

                    return Col(content).WithStyle(scopeStyle);
                }
            }, isStatic: true);

        private static IComponent Object([NotNull] JToken initialValue, [NotNull] Action<JToken> onValueChanged, IComponent prefix = null, IComponent suffix = null) =>
            new Component(ctx =>
            {
                var obj = (JObject)initialValue;
                
                var items = ctx.Use(() => new MutableList<(string name, JToken value)>(obj.Properties().Select(p => (p.Name, p.Value))));

                return Col(
                    Start(),
                    Content(),
                    End()
                );

                IComponent Start() => Row(
                    CU.Switch(prefix != null, () => prefix, () => null),
                    Text("{").WithStyle(fillStyle).WithStyle(scopeBracketsStyle),
                    CU.Switch(suffix != null, () => suffix, () => null)
                );
                
                IComponent End() =>
                    Row(content: IComponent.Seq(
                        Text("}").WithStyle(scopeBracketsStyle),
                        AddField()
                    ));
                
                IComponent Content()
                {
                    var content = items.IndexedValues.Select((item, index) =>
                    {
                        var (id, tokenItem) = item;
                        var (name, token) = tokenItem;

                        return ObjectItem(
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
                        ).Id((int)id + 1);
                    });

                    return Col(content).WithStyle(scopeStyle);
                }
                
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
                )
                {
                    return Row(
                        // TODO: use custom component to better control sizing
                        String(name, v => onNameChanged(v.Value<string>()))
                            .WithStyle(new(flexGrow: 0)),
                        DynamicTypeValue(initialValue, onValueChanged,
                            Row(
                                Button(onRequestRemove, "x"),
                                Button(onRequestMoveUp, "^"),
                                Button(onRequestMoveDown, "v")
                            )
                        )
                    ).WithStyle(new(alignItems: Align.FlexStart));
                }

                IComponent AddField() => new Component(fCtx =>
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
                    ).WithStyle(new(minWidth: 32));
                });
            });
    }
}