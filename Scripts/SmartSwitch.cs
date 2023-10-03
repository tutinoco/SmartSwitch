using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace tutinoco
{
    public enum SwitchType
    {
        Switch,
        Button,
        Pickup,
        ToggleTrigger,
        PushTrigger,
        Area,
        Join,
    }

    public enum LinkType
    {
        Sync,
        Radio,
        Pulse,
    }

    public class SmartSwitch : SimpleNetworkBehaviour
    {
        [System.NonSerialized] public bool isOn;

        [Header("スイッチの種類を設定")]
        public SwitchType type;

        [Header("スイッチの初期状態を設定")]
        public bool defaultOn;

        [Header("スイッチON/OFF時に有効にするオブジェクトを設定")]
        public bool globalActiveSwitch;
        public string activeGroupOnSwitch;
        public string activeGroupOffSwitch;
        public GameObject[] activeObjectsOnSwitch;
        public GameObject[] activeObjectsOffSwitch;

        [Header("スイッチ切り替え時に鳴らす音を設定")]
        public bool globalSound;
        public AudioClip onSound;
        public AudioClip offSound;
        public bool playParticleWithSound;

        [Header("スイッチON/OFF時に実行するイベントを設定")]
        public bool globalEvent;
        public string onEventTarget;
        public string onEventName;
        public string[] onEventValues;
        private object[] onEventArgs;
        public string offEventTarget;
        public string offEventName;
        public string[] offEventValues;
        private object[] offEventArgs;

        [Header("グループ化するスイッチの名前を指定（ワイルドカード使用可）")]
        public string group;

        [Header("他のスイッチとリンクして動作させるスイッチを設定")]
        public LinkType linkType;
        public bool enableAutoLink;
        public bool enableLinkedActiveGroup;
        public bool enableLinkedSound;
        public bool enableLinkedEvent;

        [Header("押したスイッチを自動で戻すタイマー（0なら無効、30なら30フレーム後に戻ります）")]
        public int switchBackFrame;
        private int switchBackCount;
        private bool defaultState;

        [Header("スイッチを操作できる人を登録（空なら誰でも操作できます）")]
        public string[] memberships;

        [Header("コンポーネントを手動で設定（省略すると自動的に取得します）")]
        public AudioSource audioSource;
        public ParticleSystem particle;
        public Collider collider;

        private bool isInArea;
        private bool isTrigger;

        void Start()
        {
            isOn = defaultOn;
            SimpleNetworkInit();

            if( collider == null ) collider = GetComponent<Collider>();
            if( audioSource == null ) audioSource = GetComponent<AudioSource>();
            if( particle == null ) particle = GetComponent<ParticleSystem>();

            if( type==SwitchType.Button && switchBackFrame<=0 ) switchBackFrame=8;

            onEventArgs = new object[onEventValues.Length];
            for(var i=0; i<onEventArgs.Length; i++) onEventArgs[i] = Parse(onEventValues[i]);
            if( onEventArgs.Length == 0 ) onEventArgs = none;

            offEventArgs = new object[offEventValues.Length];            
            for(var i=0; i<offEventArgs.Length; i++) offEventArgs[i] = Parse(offEventValues[i]);
            if( offEventArgs.Length == 0 ) offEventArgs = none;
        }

        void Update()
        {
            if( type == SwitchType.Button ) DisableInteractive = defaultOn!=isOn;
            if( type == SwitchType.Pickup || type == SwitchType.ToggleTrigger || type == SwitchType.PushTrigger || type == SwitchType.Join ) DisableInteractive = true;

            if( switchBackCount >= 0 ) switchBackCount--;
            if( switchBackCount == 0 ) Toggle();

            if( type==SwitchType.Area && collider!=null ) {
                DisableInteractive = true;
                collider.isTrigger = true;
                Vector3 playerPos = Networking.LocalPlayer.GetPosition();
                bool b = 0.1f > Vector3.Distance(collider.ClosestPoint(playerPos), playerPos);
                if ( isInArea != b ) { isInArea=b; Toggle(); }
            }
        }

        public override void OnPickup()
        {
            if( type != SwitchType.Pickup ) return;
            Toggle();
        }

        public override void OnDrop()
        {
            if( type != SwitchType.Pickup ) return;
            Toggle();
        }

        public override void OnPickupUseUp()
        {
            if( !(type==SwitchType.ToggleTrigger || type==SwitchType.PushTrigger) ) return;
            if( isTrigger ) return;
            isTrigger = true;
            Toggle();
        }

        public override void OnPickupUseDown()
        {
            isTrigger = false;
            if( type==SwitchType.ToggleTrigger ) Toggle();
        }

        public override void OnPlayerJoined( VRCPlayerApi player )
        {
            if( Networking.IsOwner(gameObject) && type == SwitchType.Join ) Switch(true);
        }

        public override void OnPlayerLeft( VRCPlayerApi player )
        {
            if( Networking.IsOwner(gameObject) && type == SwitchType.Join ) Switch(false);
        }

        private void UpdateObjects()
        {
            foreach(var obj in activeObjectsOnSwitch) if( obj!=null ) obj.SetActive(isOn);
            foreach(var obj in activeObjectsOffSwitch) if( obj!=null ) obj.SetActive(!isOn);
            if(activeGroupOnSwitch!="") foreach(var b in GetBehaviours(activeGroupOnSwitch) ) b.gameObject.SetActive(isOn);
            if(activeGroupOffSwitch!="") foreach(var b in GetBehaviours(activeGroupOffSwitch) ) b.gameObject.SetActive(!isOn);
        }

        public void UpdateLinks()
        {
            foreach( var b in GetBehaviours(group) ) {
                var ss = b.gameObject.GetComponent<SmartSwitch>(); // isがつかえない？
                if( ss == null || ss == this ) continue;
                if( linkType==LinkType.Sync && isOn!=ss.IsOn() ) ss.ExecEvent("_Sync", isOn);
                if( linkType==LinkType.Radio && ss.IsOn() ) ss.ExecEvent("_Sync", false);
                if( linkType==LinkType.Pulse ) ss.ExecEvent("_Sync", !ss.IsOn());
                if( enableLinkedEvent ) ss.ExecEvent("_Exec");
            }
        }

        public override void OnSimpleNetworkReady() { UpdateObjects(); }

        public override void Interact() { Toggle(); }

        public bool IsOn() { return isOn; }

        public void SetOnEventValues( object[] values ) { onEventArgs = values; }
        public object[] GetOnEventValues() { return onEventArgs; }
        public void SetOffEventValues( object[] values ) { offEventArgs = values; }
        public object[] GetOffEventValues() { return offEventArgs; }

        public void Switch( bool flg )
        {
            if( memberships.Length > 0 ) {
                bool f = false;
                string name = Networking.LocalPlayer.displayName;
                foreach (var m in memberships) if(m == name) { f=true; break; }
                if(!f) return;
            }

            if( switchBackCount==-1 && isOn!=flg ) switchBackCount = switchBackFrame;
            isOn = flg;

            var syncSendto = globalActiveSwitch ? SendTo.All : SendTo.Self;
            var evObj = SendEvent(syncSendto, "_Sync", isOn);
            if( syncSendto == SendTo.All ) SetJoinEvent(evObj);

            var playSendto = globalSound ? SendTo.All : SendTo.Self;
            SendEvent(playSendto, "_Play", isOn);

            ExcuteEvent();

            if( enableAutoLink ) UpdateLinks();
        }

        public void Toggle() { Switch(!isOn); }

        public object Parse( string str )
        {
            if( str.Length == 0 ) return str;

            char suffix = str[str.Length-1];
            string value = str.Substring(0, str.Length-1);

            switch (suffix) {
                case 'u': return uint.Parse(value);
                case 'l':
                    if( value == "nul" ) return null;
                    if( value[value.Length-1] != 'u' ) return long.Parse(value);
                    return ulong.Parse(value.Substring(0, value.Length-1));
                case 'f': return float.Parse(value);
                case 'd': return double.Parse(value);
                case 'm': return decimal.Parse(value);
                case '"': return value.Substring(1);
                case 'e':
                    if( value == "tru" ) return true;
                    if( value == "fals" ) return false;
                    break;
                default:
                    if( char.IsDigit(suffix) ) {
                        int count = 0;
                        foreach(char c in value) if(c == '.') count++;
                        if( count > 0 ) return float.Parse(value);
                        return int.Parse(value);
                    }
                    break;
            }

            return str;
        }

        public void ExcuteEvent()
        {
            var sendto = globalEvent ? SendTo.All : SendTo.Self;
            if( isOn && onEventName!="" ) SendEvent(sendto, onEventName, onEventArgs, onEventTarget);
            if( !isOn && offEventName!="" ) SendEvent(sendto, offEventName, offEventArgs, offEventTarget);
        }

        public override void ReceiveEvent( string name )
        {
            if( name == "_Exec" ) ExcuteEvent();

            if( name == "_Sync" ) { 
                isOn = GetBool();
                UpdateObjects();
            }

            if( name == "_Play" && audioSource!=null ) {
                bool flg = GetBool();
                var sound = flg ? onSound : offSound;
                if( sound!=null ) audioSource.PlayOneShot(sound);
                if( particle != null && playParticleWithSound && !(type == SwitchType.Button && !flg) ) particle.Play();
            }

            if( name == "Portal" ) {
                GameObject target = null;
                foreach(var b in GetBehaviours(group) ) if( b.gameObject != gameObject ) { target = b.gameObject; break; }
                if( target == null ) return;
                var pos = target.transform.position + me.GetPosition() - transform.position;
                Quaternion rot = target.transform.rotation * Quaternion.Inverse(transform.rotation) * me.GetRotation();
                //rot = me.GetRotation();
                me.TeleportTo(pos, rot);
            }
        }
    }
}
