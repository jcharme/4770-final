using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using Project;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KaijuSolutions.Agents;
using KaijuSolutions.Agents.Movement;
// using UnityEngine.AI;
using TMPro;

public class LLMBridge : MonoBehaviour
    
{
    [Header("Settings")] 
    [SerializeField] private string apiKey;
    private string mainModel = "gemini-3.1-flash-lite-preview"; 
    private string fallbackModel = "gemini-2.5-flash";
    
    [Header("UI Setup")]
    public TMP_InputField chatInput;

    [Header("Game Objects")]
    // reference to ghost
    public KaijuAgent ghostAgent;

    // This is the specific function our physical button will press
    public void OnSendButtonClicked()
    {
        if (chatInput != null && !string.IsNullOrEmpty(chatInput.text))
        {
            RequestAction(chatInput.text); // Send the text to the LLM
            chatInput.text = "";           // Clear the text box so it's ready for the next command
        }
    }

    void Awake()
    {
        apiKey = APIKeys.GeminiKey;
    }

    private string[] roomDescriptions = {
        "ID: Entrance | Position: Far East. | Connections: [Hall-East (West)] | Features: Checkered floor (no carpet), main double doors.",
        "ID: Hall-East | Position: East-Central. | Connections: [Dining (North), Entrance (East), Bedroom-Large (South)] | Features: Blue carpet, wooden floor, furnace.",
        "ID: Dining | Position: North-East. | Connections: [Kitchen (West), Hall-East (South)] | Features: Checkered floor (no carpet), large table.",
        "ID: Kitchen | Position: North-Central. | Connections: [Dining (East), Corridor (South)] | Features: Green rug, stone tiles, stovetop.",
        "ID: Corridor | Position: Central transition. | Connections: [Kitchen (North), Library (West), Bedroom-Small-North (East), Bedroom-Small-South (East)] | Features: Long blue rugs, plus one yellow rug at the bottom near the Library entrance.",
        "ID: Library | Position: West-Central. | Connections: [Corridor (East), Hall-West (South)] | Features: Large yellow rug, chairs, book-filled walls.",
        "ID: Hall-West | Position: Far West. | Connections: [Library (North), Bathroom-North (North)] | Features: Red rugs (only red ones in the house), lounge chairs.",
        "ID: Bathroom-North | Position: North-West corner. | Connections: [Hall-West (South)] | Features: Green carpet, bathtub, tiled floor.",
        "ID: Bedroom-Large | Position: South-East. | Connections: [Hall-East (North), Bathroom-South (West)] | Features: Purple carpet, large bed.",
        "ID: Bathroom-South | Position: South-Central. | Connections: [Bedroom-Large (East)] | Features: White tiles (no carpet), bathtub.",
        "ID: Bedroom-Small-North | Position: Mid-East. | Connections: [Corridor (West)] | Features: Bed, dresser, wooden floor.",
        "ID: Bedroom-Small-South | Position: Mid-East. | Connections: [Corridor (West)] | Features: Bed, dresser, wooden floor."
    };

    // This is the function the UI Button calls
    public void RequestAction(string userInput)
    {
        StartCoroutine(CallGemini(userInput));
    }

    // Network request
    IEnumerator CallGemini(string userInput)
    {
        // 1. Try the Main Model
        Debug.Log($"[Attempt 1] Trying {mainModel}...");
        yield return SendRequest(userInput, mainModel, (success, response) => {
            if (success) {
                ProcessResponse(response);
            } else {
                // 2. If Main fails with 503, try Fallback
                Debug.LogWarning($"[503] {mainModel} busy. Switching to {fallbackModel}...");
                StartCoroutine(SendRequest(userInput, fallbackModel, (fallbackSuccess, fallbackResponse) => {
                    if (fallbackSuccess) {
                        ProcessResponse(fallbackResponse);
                    } else {
                        Debug.LogError("FATAL: Both Gemini models are currently unavailable.");
                    }
                }));
            }
        });
    }

    IEnumerator SendRequest(string userInput, string targetModel, Action<bool, string> callback)
    {
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{targetModel}:generateContent?key={apiKey.Trim()}";
        
        // 1. Join your combined descriptions (the ones with Position and Features)
        string mapContext = string.Join("\n", roomDescriptions);

        // 2. Build the prompt with strict Goal alignment
        string systemPrompt = "You are a ghost in a haunted house. Use the following map to navigate:\n\n" + 
                       mapContext + "\n\n" +
                       "GOAL SELECTION RULES:\n" +
                       "- If the user wants to scare or haunt: use goal 'ScareResident'.\n" +
                       "- If the user wants to go to a room: use goal 'MoveTo<roomID>' (e.g., 'MoveToBedroom-North').\n" +
                       // "- If the user wants to retreat or hide: use goal 'HideInKitchen'.\n" +
                       "- If no specific goal is mentioned: use goal 'none'.\n\n" +
                       "ROOM SELECTION RULES:\n" +
                       "- Identify the room by ID (e.g., 'Hall-East').\n" +
                       "- Use 'Position' and 'Features' to resolve vague requests (e.g., 'the room with the yellow rug' is Library).\n\n" +
                       "RESPONSE FORMAT:\n" +
                       "Return ONLY a JSON object: {\"room\": \"RoomID\", \"goal\": \"GoalName\", \"target\": \"TargetName\"}.\n" +
                       "If hunting a person, set 'target' to their name (e.g. 'Resident'). Otherwise leave it blank.";
        
        string combinedPrompt = systemPrompt + userInput;

        string escapedPrompt = combinedPrompt
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");

        string jsonPayload = "{\"contents\": [{\"parts\":[{\"text\":\"" + escapedPrompt + "\"}]}]}";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                callback(true, request.downloadHandler.text);
            }
            else if (request.responseCode == 503)
            {
                // Return false so the fallback logic triggers
                callback(false, null);
            }
            else
            {
                Debug.LogError($"Hard Error on {targetModel}: {request.error}");
                callback(false, null);
            }
        }
    }


    // The parser: extracts JSON and triggers game logic
    void ProcessResponse(string rawResponse)
    {
        Debug.Log("<color=green>LLM Response received:</color> " + rawResponse);
        GeminiResponse responseData = JsonUtility.FromJson<GeminiResponse>(rawResponse);
        
        if (responseData?.candidates != null && responseData.candidates.Count > 0)
        {
            // Extract the text that the AI generated
            string aiText = responseData.candidates[0].content.parts[0].text;
            
            // Clean it 
            aiText = aiText.Replace("```json", "").Replace("```", "").Trim();
            Debug.Log("<color=green>Cleaned JSON from LLM:</color> " + aiText);
            
            // Parse the AI's JSON into  GhostCommand 
            GhostCommand cmd = JsonUtility.FromJson<GhostCommand>(aiText);
            
            if (cmd == null)
            {
                Debug.LogError("Failed to parse GhostCommand from cleaned JSON.");
                return;
            }

            // Handle GOAP goals if present
            if (!string.IsNullOrEmpty(cmd.goal) && cmd.goal.ToLower() != "none")
            {
                Debug.Log($"<color=green>LLM Bridge:</color> Triggering OnLLMResponse with goal: {cmd.goal}");
                OnLLMResponse(aiText);
            }
            // Fallback to direct movement if goal is missing but room is present
            else if (!string.IsNullOrEmpty(cmd.room))
            {
                Debug.Log($"<color=green>LLM Bridge:</color> Falling back to MoveGhost with room: {cmd.room}");
                MoveGhost(cmd.room);
            }
            else
            {
                Debug.LogWarning("LLM returned no goal and no room.");
            }
        }
        else
        {
            Debug.LogError("Failed to parse Gemini response candidates.");
        }
    }

    public void OnLLMResponse(string intentJSON)
    {
        GhostCommand cmd = JsonUtility.FromJson<GhostCommand>(intentJSON);
        Dictionary<string, object> desiredGoal = new Dictionary<string, object>();
        
        // find ghost brain?
        GoapController ghostAI = ghostAgent != null ? ghostAgent.GetComponent<GoapController>() : null;
        if (ghostAI == null)
        {
            Debug.LogError("GOAP component not found on ghostAgent!");
            return;
        }
        
        Debug.Log($"<color=green>LLM Bridge:</color> Processing goal '{cmd.goal}' for target '{cmd.target}'");

        if (cmd.goal.Contains("ScareResident"))
        {
            desiredGoal.Add("ResidentScared", true);
            
            GameObject victim = null;
            
            // Try to find the exact target the LLM named
            if (!string.IsNullOrEmpty(cmd.target))
            {
                victim = GameObject.Find(cmd.target);
                if (victim != null) Debug.Log($"<color=green>LLM Bridge:</color> Found specific target: {cmd.target}");
            }
            
            // Fallback: If LLM didn't give a name, just grab ANY ResidentController in the scene
            if (victim == null)
            {
                ResidentController anyResident = FindFirstObjectByType<ResidentController>();
                if (anyResident != null) 
                {
                    victim = anyResident.gameObject;
                    Debug.Log($"<color=green>LLM Bridge:</color> Found fallback target: {victim.name}");
                }
            }
            
            // Lock onto the victim!
            if (victim != null)
            {
                ghostAI.targetVictim = victim.transform;
            }
            else
            {
                Debug.LogError("LLM requested a scare, but could not find a Resident in the scene!");
                return; // Abort planning so we don't crash
            }
        }
        else if (cmd.goal.Contains("HideInShadows") || cmd.goal.Contains("HideInKitchen"))
        {
            desiredGoal.Add("InKitchen", true);
            desiredGoal.Add("IsHidden", true);
        }

        // Request the plan
        if (desiredGoal.Count > 0)
        {
            Debug.Log($"<color=green>LLM Bridge:</color> Requesting plan from GOAP for {desiredGoal.Count} conditions.");
            ghostAI.RequestPlan(desiredGoal);
        }
        else if (!string.IsNullOrEmpty(cmd.room))
        {
            // If no recognized goal, just move to the room
            Debug.Log($"<color=green>LLM Bridge:</color> No recognized goal, moving to room: {cmd.room}");
            MoveGhost(cmd.room);
        }
    }
    
    // The game logic
    void MoveGhost(string targetRoom)
    {
        Debug.Log("COMMAND RECEIVED: Move to " + targetRoom);
        
        if (ghostAgent != null)
        {
            GoapController ghostAI = ghostAgent.GetComponent<GoapController>();
            if (ghostAI != null)
            {
                // Request a plan to be in the target room
                ghostAI.MoveToRoom(targetRoom);
            }
            else
            {
                Debug.LogError("GOAP component not found on ghostAgent!");
                
                // Fallback to direct movement if GOAP is missing
                GameObject waypoint = GameObject.Find("POI_" + targetRoom);
                if (waypoint != null)
                {
                    ghostAgent.PathFollow(waypoint.transform.position, clear: true);
                    ghostAgent.ObstacleAvoidance(clear: false);
                }
            }
        }
    }
}

// Data structures for JSONUtility
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