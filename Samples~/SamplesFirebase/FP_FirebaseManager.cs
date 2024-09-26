
using UnityEngine;


namespace FuzzPhyte.Utility.Analytics.Samples.Firebase
{
    public class FP_FirebaseManager : MonoBehaviour
    {
        //we are using Resources Load so don't use the file extension as it treats this as a text asset
        public string FireBaseConfigFileName = "firebaseConfig";
        [Tooltip("Class reference for caching the firebase config")]
        public FP_FireConfig config;

        //firebase variables
        
       // private DatabaseReference _databaseReference;
       // private FirebaseConfigLoader _configLoader;


        void Start()
        {
            LoadConfig();
        }

        void LoadConfig()
        {
            var jsonConfig = Resources.Load<TextAsset>(FireBaseConfigFileName);
            if (jsonConfig != null)
            {
                config = JsonUtility.FromJson<FP_FireConfig>(jsonConfig.ToString());
                Debug.Log("Firebase config loaded successfully");
            }
            else
            {
                Debug.LogError($"firebaseConfig.json file not found in Resources folder");
            }
            
        }
    }
}
