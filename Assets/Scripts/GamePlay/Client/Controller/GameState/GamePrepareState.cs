using GamePlay.Client.Model;

namespace GamePlay.Client.Controller.GameState
{
    public class GamePrepareState : ClientState
    {
        public int[] Points;
        public string[] Names;
        public override void OnClientStateEnter()
        {
            // 新开一局游戏时清空上一场的游戏记录。
            GameRecordStore.Reset();
            // assign round status
            controller.AssignRoundStatus(CurrentRoundStatus);
            // update data
            CurrentRoundStatus.UpdatePoints(Points);
            CurrentRoundStatus.UpdateNames(Names);
            // send ready message
            ClientBehaviour.Instance.ClientReady();
        }

        public override void OnClientStateExit()
        {
        }

        public override void OnStateUpdate()
        {
        }
    }
}
