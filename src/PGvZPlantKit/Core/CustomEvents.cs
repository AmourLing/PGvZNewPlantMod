using System;
using System.Runtime.CompilerServices;
using Lawn;

namespace PGvZPlantKit
{
    /// <summary>
    /// 棋盘级事件：开局、新一波、僵尸死亡、模组植物出现/消失。都从游戏线程上的钩子里触发，
    /// 面向两层收听者：场上每个自定义植物的 OnLevelStart/OnWave/OnZombieDied，
    /// 以及 PlantKit.LevelStarted/WaveStarted/ZombieDied/PlantPlanted/PlantRemoved 上的
    /// 全局处理器（含 IronPython）。
    /// </summary>
    internal static class CustomEvents
    {
        private class BoardState
        {
            internal int LastWave = -1;
            internal bool Started;
        }

        // Board 重开时会换成新实例，弱表随旧 Board 一起回收。
        private static readonly ConditionalWeakTable<Board, BoardState> States =
            new ConditionalWeakTable<Board, BoardState>();

        public static event Action<Board> LevelStarted;
        public static event Action<Board, int> WaveStarted;
        public static event Action<Zombie> ZombieDied;
        /// <summary>场上出现一株模组植物：种下、附件生成或存档恢复都会触发一次。</summary>
        public static event Action<Board, Plant, CustomPlantDef> PlantPlanted;
        /// <summary>一株模组植物离开棋盘（被铲、被吃、被压、关卡结束清场、附件本体消失）。</summary>
        public static event Action<Board, Plant, CustomPlantDef> PlantRemoved;

        /// <summary>挂在 Board.UpdateGame 之后：只看场景与波号的变化，不做别的。</summary>
        public static void OnBoardUpdate(Board board)
        {
            if (board == null)
            {
                return;
            }
            BoardState state = States.GetOrCreateValue(board);
            if (!state.Started)
            {
                if (board.mApp == null || board.mApp.mGameScene != GameScenes.Playing)
                {
                    return;
                }
                state.Started = true;
                state.LastWave = board.mCurrentWave;
                CustomPlantRegistry.DispatchLevelStart(board);
                RaiseBoard(LevelStarted, board);
                return;
            }
            if (board.mCurrentWave != state.LastWave)
            {
                state.LastWave = board.mCurrentWave;
                CustomPlantRegistry.DispatchWave(board, board.mCurrentWave);
                RaiseWave(WaveStarted, board, board.mCurrentWave);
            }
        }

        public static void OnZombieDied(Zombie zombie)
        {
            if (zombie == null)
            {
                return;
            }
            CustomPlantRegistry.DispatchZombieDied(zombie.mBoard, zombie);
            RaiseZombie(ZombieDied, zombie);
        }

        public static void OnPlantPlanted(Plant plant, CustomPlantDef def) =>
            RaisePlant(PlantPlanted, plant, def);

        public static void OnPlantRemoved(Plant plant, CustomPlantDef def) =>
            RaisePlant(PlantRemoved, plant, def);

        private static void RaisePlant(
            Action<Board, Plant, CustomPlantDef> evt, Plant plant, CustomPlantDef def)
        {
            if (evt == null || plant == null)
            {
                return;
            }
            Board board = plant.mBoard;
            foreach (Delegate handler in evt.GetInvocationList())
            {
                try
                {
                    ((Action<Board, Plant, CustomPlantDef>)handler)(board, plant, def);
                }
                catch (Exception ex)
                {
                    ModLog.Error("plant event handler failed", ex);
                }
            }
        }

        private static void RaiseBoard(Action<Board> evt, Board board)
        {
            if (evt == null)
            {
                return;
            }
            foreach (Delegate handler in evt.GetInvocationList())
            {
                try
                {
                    ((Action<Board>)handler)(board);
                }
                catch (Exception ex)
                {
                    ModLog.Error("PlantKit.LevelStarted handler failed", ex);
                }
            }
        }

        private static void RaiseWave(Action<Board, int> evt, Board board, int wave)
        {
            if (evt == null)
            {
                return;
            }
            foreach (Delegate handler in evt.GetInvocationList())
            {
                try
                {
                    ((Action<Board, int>)handler)(board, wave);
                }
                catch (Exception ex)
                {
                    ModLog.Error("PlantKit.WaveStarted handler failed", ex);
                }
            }
        }

        private static void RaiseZombie(Action<Zombie> evt, Zombie zombie)
        {
            if (evt == null)
            {
                return;
            }
            foreach (Delegate handler in evt.GetInvocationList())
            {
                try
                {
                    ((Action<Zombie>)handler)(zombie);
                }
                catch (Exception ex)
                {
                    ModLog.Error("PlantKit.ZombieDied handler failed", ex);
                }
            }
        }
    }
}
