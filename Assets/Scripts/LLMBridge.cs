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

    void Awake()
    {
        apiKey = APIKeys.GeminiKey;
        if (ghostAgent != null)
        {
            ghostAI = ghostAgent.GetComponent<GhostController>();
            if (ghostAI == null) Debug.LogError("LLMBridge: GhostController not found on ghostAgent!");
        }
        else
        {
            Debug.LogError("LLMBridge: ghostAgent is not assigned in the inspector!");
        }
    }

    public void OnSendButtonClicked()
    {
        if (chatInput != null && !string.IsNullOrEmpty(chatInput.text))
        {
            RequestAction(chatInput.text);
            chatInput.text = "";
        }
    }

    public void RequestAction(string userInput) => StartCoroutine(CallGemini(userInput));

    private IEnumerator CallGemini(string userInput)
    {
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey.Trim()}";
        
        string systemPrompt = "You are a ghost. Map:\n" + string.Join("\n", roomDescriptions) + "\n\n" +
                       "RESIDENTS: John, Ivan\n\n" +
                       "GOALS:\n" +
                       "- 'ScareResident': To haunt/scare John or Ivan.\n" +
                       "- 'MoveToRoom': To go to a specific room.\n\n" +
                       "FORMAT: JSON {\"room\": \"RoomID\", \"goal\": \"GoalName\", \"target\": \"TargetName\"}";
        
        string jsonPayload = "{\"contents\": [{\"parts\":[{\"text\":\"" + 
                             (systemPrompt + "\n\nUser: " + userInput).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + 
                             "\"}]}]}";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"<color=yellow>LLM Raw Response:</color> {request.downloadHandler.text}");
                GeminiResponse responseData = JsonUtility.FromJson<GeminiResponse>(request.downloadHandler.text);
                if (responseData?.candidates?.Count > 0)
                {
                    string aiText = responseData.candidates[0].content.parts[0].text;
                    aiText = aiText.Replace("```json", "").Replace("```", "").Trim();
                    Debug.Log($"<color=yellow>LLM Parsed Command:</color> {aiText}");
                    GhostCommand cmd = JsonUtility.FromJson<GhostCommand>(aiText);
                    if (cmd != null) ExecuteCommand(cmd);
                    else Debug.LogError("LLMBridge: Failed to parse GhostCommand from AI response.");
                }
            }
            else
            {
                Debug.LogError($"LLMBridge: Request failed — {request.result} | {request.error}");
            }
        }
    }

    private void ExecuteCommand(GhostCommand cmd)
    {
        if (ghostAI == null) return;

        if (cmd.goal == "ScareResident")
        {
            ResidentController victim = FindVictim(cmd.target, cmd.room);
            if (victim != null)
                ghostAI.BeginHunt(victim.transform);   // <-- one call, sets flag + starts plan
        }
        else if (!string.IsNullOrEmpty(cmd.room))
        {
            // Plain movement — no hunting
            ghostAI.RequestPlan(new Dictionary<string, object> { { "CurrentRoom", cmd.room } });
        }
    }

    private ResidentController FindVictim(string targetName, string roomName)
    {
        var residents = FindObjectsByType<ResidentController>(FindObjectsSortMode.None);
        if (residents.Length == 0) return null;

        if (!string.IsNullOrEmpty(targetName) && targetName.ToLower() != "resident")
        {
            var match = residents.FirstOrDefault(r => r.gameObject.name.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0);
            if (match != null) return match;
        }

        if (!string.IsNullOrEmpty(roomName))
        {
            GameObject poi = GameObject.Find("POI_" + roomName);
            if (poi != null) return residents.OrderBy(r => Vector3.Distance(r.transform.position, poi.transform.position)).FirstOrDefault();
        }

        return residents.OrderBy(r => Vector3.Distance(r.transform.position, ghostAI.transform.position)).FirstOrDefault();
    }

    private string[] roomDescriptions = {
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
}

[Serializable] public class GhostCommand { public string room; public string goal; public string target; }
[Serializable] public class GeminiResponse { public List<Candidate> candidates; }
[Serializable] public class Candidate { public Content content; }
[Serializable] public class Content { public List<Part> parts; }
[Serializable] public class Part { public string text; }
