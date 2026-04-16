using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KaijuSolutions.Agents;
using Project;
using TMPro;

/// <summary>
/// Facilitates communication between the Unity game environment and the Gemini LLM API.
/// Parses natural language input into structured ghost commands.
/// </summary>
public class LLMBridge : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private string apiKey;
    private string model = "gemini-3.1-flash-lite-preview";

    [Header("UI Setup")]
    public TMP_InputField chatInput;

    [Header("Game Objects")]
    public KaijuAgent ghostAgent;
    private GhostController ghostAI;

    private readonly string[] roomDescriptions =
    {
        "ID: Entrance | Features: Checkered floor, main double doors.",
        "ID: Hall-East | Features: Blue carpet, furnace.",
        "ID: Dining | Features: Checkered floor, large table.",
        "ID: Kitchen | Features: Green rug, stone tiles.",
        "ID: Corridor | Features: Long blue rugs, yellow rug near Library.",
        "ID: Library | Features: Large yellow rug, book-filled walls.",
        "ID: Hall-West | Features: Red rugs, lounge chairs.",
        "ID: Bathroom-North | Features: Green carpet, bathtub.",
        "ID: Bedroom-Large | Features: Purple carpet, large bed.",
        "ID: Bathroom-South | Features: White tiles, bathtub.",
        "ID: Bedroom-Small-North | Features: Bed, dresser.",
        "ID: Bedroom-Small-South | Features: Bed, dresser."
    };

    /// <summary>
    /// Initializes the bridge and validates dependencies.
    /// </summary>
    private void Awake()
    {
        apiKey = APIKeys.GeminiKey;
        if (ghostAgent != null)
        {
            ghostAI = ghostAgent.GetComponent<GhostController>();
            if (ghostAI == null)
            {
                Debug.LogError("LLMBridge: GhostController not found on ghostAgent.");
            }
        }
        else
        {
            Debug.LogError("LLMBridge: ghostAgent is not assigned in the inspector.");
        }
    }

    /// <summary>
    /// UI Callback for the send button.
    /// </summary>
    public void OnSendButtonClicked()
    {
        if (chatInput != null && !string.IsNullOrEmpty(chatInput.text))
        {
            RequestAction(chatInput.text);
            chatInput.text = "";
        }
    }

    /// <summary>
    /// Public entry point to request an action from the LLM.
    /// </summary>
    /// <param name="userInput">The natural language command from the user.</param>
    public void RequestAction(string userInput)
    {
        StartCoroutine(CallGemini(userInput));
    }

    /// <summary>
    /// Sends a prompt to the Gemini API and processes the response.
    /// </summary>
    private IEnumerator CallGemini(string userInput)
    {
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey.Trim()}";

        string systemPrompt = "You are a ghost. Map:\n" + string.Join("\n", roomDescriptions) + "\n\n" +
                              "RESIDENTS: John, Ivan\n\n" +
                              "GOALS:\n" +
                              "- 'ScareResident': To haunt/scare John or Ivan.\n" +
                              "- 'MoveToRoom': To go to a specific room.\n\n" +
                              "FORMAT: JSON {\"room\": \"RoomID\", \"goal\": \"GoalName\", \"target\": \"TargetName\"}";

        // Construct raw JSON payload for the Gemini API
        string sanitizedPrompt = (systemPrompt + "\n\nUser: " + userInput).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        string jsonPayload = "{\"contents\": [{\"parts\":[{\"text\":\"" + sanitizedPrompt + "\"}]}]}";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                GeminiResponse responseData = JsonUtility.FromJson<GeminiResponse>(request.downloadHandler.text);
                if (responseData?.candidates?.Count > 0)
                {
                    string aiText = responseData.candidates[0].content.parts[0].text;
                    // Clean up potential markdown formatting from the LLM response
                    aiText = aiText.Replace("```json", "").Replace("```", "").Trim();

                    GhostCommand cmd = JsonUtility.FromJson<GhostCommand>(aiText);
                    if (cmd != null)
                    {
                        ExecuteCommand(cmd);
                    }
                    else
                    {
                        Debug.LogError("LLMBridge: Failed to parse GhostCommand from AI response.");
                    }
                }
            }
            else
            {
                Debug.LogError($"LLMBridge: API Request failed — {request.result} | {request.error}");
            }
        }
    }

    /// <summary>
    /// Translates a structured GhostCommand into game-world actions.
    /// </summary>
    private void ExecuteCommand(GhostCommand cmd)
    {
        if (ghostAI == null)
        {
            return;
        }

        if (cmd.goal == "ScareResident")
        {
            ResidentController victim = FindVictim(cmd.target, cmd.room);
            if (victim != null)
            {
                Debug.Log($"<color=yellow>Ghost Decision:</color> Hunting {victim.name} in {cmd.room}.");
                ghostAI.BeginHunt(victim.transform);
            }
        }
        else if (!string.IsNullOrEmpty(cmd.room))
        {
            Debug.Log($"<color=yellow>Ghost Decision:</color> Moving to {cmd.room}.");
            ghostAI.RequestPlan(new Dictionary<string, object> { { "CurrentRoom", cmd.room } });
        }
    }

    /// <summary>
    /// Finds the most suitable victim based on the LLM's suggested target or room.
    /// </summary>
    private ResidentController FindVictim(string targetName, string roomName)
    {
        var residents = FindObjectsByType<ResidentController>(FindObjectsSortMode.None);
        if (residents.Length == 0)
        {
            return null;
        }

        // Try to match by name
        if (!string.IsNullOrEmpty(targetName) && targetName.ToLower() != "resident")
        {
            var match = residents.FirstOrDefault(r => r.gameObject.name.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0);
            if (match != null)
            {
                return match;
            }
        }

        // Try to match by proximity to a room POI
        if (!string.IsNullOrEmpty(roomName))
        {
            GameObject poi = GameObject.Find("POI_" + roomName);
            if (poi != null)
            {
                return residents.OrderBy(r => Vector3.Distance(r.transform.position, poi.transform.position)).FirstOrDefault();
            }
        }

        // Default to the closest resident
        return residents.OrderBy(r => Vector3.Distance(r.transform.position, ghostAI.transform.position)).FirstOrDefault();
    }
}

#region Data Models

[Serializable]
public class GhostCommand
{
    public string room;
    public string goal;
    public string target;
}

[Serializable]
public class GeminiResponse
{
    public List<Candidate> candidates;
}

[Serializable]
public class Candidate
{
    public Content content;
}

[Serializable]
public class Content
{
    public List<Part> parts;
}

[Serializable]
public class Part
{
    public string text;
}

#endregion
