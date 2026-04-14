using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using Project;
using System;
using System.Collections.Generic;
using System.Text;
using KaijuSolutions.Agents;
using UnityEngine.AI;
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
    public NavMeshAgent ghostAgent;
    
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
    
    // Prompt forces AI to output JSON
    private string systemPrompt = "You are a ghost in a haunted house. " +
                                  "The available rooms are: Kitchen, Library, and Hallway. " +
                                  "You must respond ONLY with a JSON object in this exact format: {\"room\": \"RoomName\"}. " +
                                  "User command: ";
        
    // Start() runs automatically when you hit Play in Unity
    void Start()
    {
        // Debug.Log("🚀 Sending test request to Gemini...");
        // // Hardcode a test question here
        // RequestAction("What is the best thing about C#?"); 
    }
        
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
            
            // If it parsed successfully and the room isn't empty, trigger the Ghost!
            if (cmd != null && !string.IsNullOrEmpty(cmd.room))
            {
                MoveGhost(cmd.room);
            }
        }
        else
        {
            Debug.LogError("Failed to parse Gemini response.");
        }
    }
    
    // The game logic
    void MoveGhost(string targetRoom)
    {
        Debug.Log("COMMAND RECEIVED: Move to " + targetRoom);
        
        // Find the marker gameobject
        GameObject waypoint = GameObject.Find("POI_" + targetRoom);
        
        if (waypoint != null)
        {
            // Tell the NavMeshAgent to walk to the coordinates
            ghostAgent.SetDestination(waypoint.transform.position);
        }
        else
        {
            Debug.LogWarning("Could not find a GameObject named POI_" + targetRoom + " in the scene!");
        }
    }
}

// Data structures for JSONUtility
[Serializable]
public class GhostCommand
{
    public string room;
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