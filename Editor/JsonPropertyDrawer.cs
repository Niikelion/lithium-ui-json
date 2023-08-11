using System;
using UnityEditor;
using UI.Li.Editor;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;

namespace UI.Li.Json
{
    [CustomPropertyDrawer(typeof(SerializableJson))]
    public class JsonPropertyDrawer: ComposablePropertyDrawer
    {
        protected override IComponent Layout(SerializedProperty property)
        {
            object value = property.GetUnderlyingValue();
            
            property.serializedObject.Update();
            
            if (value is not SerializableJson json)
                throw new InvalidOperationException($"This drawer can not handle {value.GetType().FullName}");

            void UpdateValue(JToken v)
            {
                Undo.RecordObject(property.serializedObject.targetObject, "Modified Json");

                if (PrefabUtility.IsPartOfAnyPrefab(property.serializedObject.targetObject))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(property.serializedObject.targetObject);
                
                json.Value = v;

                property.serializedObject.ApplyModifiedProperties();
            }
              
            return CustomField.V(
                property,
                JsonUI.DynamicTypeValue(json.Value, UpdateValue)
            );
        }
    }
}