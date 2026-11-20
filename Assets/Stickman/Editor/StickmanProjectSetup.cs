using System.IO;
using UnityEditor;
using UnityEngine;

namespace Stickman.EditorTools
{
    // Builds the scenes automatically the first time the project is opened.
    [InitializeOnLoad]
    static class StickmanProjectSetup
    {
        static StickmanProjectSetup()
        {
            if (Application.isBatchMode || File.Exists(StickmanSceneBuilder.TrainingScenePath)) return;
            EditorApplication.update += BuildWhenEditorIsReady;
        }

        static void BuildWhenEditorIsReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            EditorApplication.update -= BuildWhenEditorIsReady;
            if (File.Exists(StickmanSceneBuilder.TrainingScenePath)) return;

            Debug.Log("[Stickman] First opening of the project: building the scenes.");
            StickmanSceneBuilder.BuildDefaultScenes();
        }
    }
}
