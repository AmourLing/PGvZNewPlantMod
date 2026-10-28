using Lawn;
using Sexy;

namespace PGvZPlantKit
{
    /// <summary>
    /// 草坪像素几何。原生 GridToPixelX 会对越界列 ASSERT，
    /// 所以"最后一格再往右一格"的边界要自己按列数推。
    /// </summary>
    public static class CustomLawn
    {
        public static int RightEdge(this Board board, int row)
        {
            int lastColumn = Constants.GRIDSIZEX > 1 ? Constants.GRIDSIZEX - 1 : 0;
            return board.GridToPixelX(lastColumn, row) + Constants.New.Board_GridCellSizeX;
        }
    }
}
