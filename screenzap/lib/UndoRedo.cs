using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace screenzap
{
    public struct UndoState
    {
        public Image Image;
        public Rectangle Selection;

        public UndoState(Image image, Rectangle selection)
        {
            Image = (Image)image.Clone();
            Selection = selection;
        }
    }
    internal class UndoRedo
    {
        private List<UndoState> _undos;
        //private List<UndoState> _redos;
        int currentUndo;

        private void Init()
        {
            _undos = new List<UndoState>();
            currentUndo = -1;
        }

        public UndoRedo()
        {
            this.Init();
        }

        public UndoRedo(UndoState state)
        {
            Init();
            Push(state);
        }
        public bool hasUndo { get { return _undos.Count > 0 && currentUndo < 0; } }
        public void Push(UndoState state)
        {
            if (_undos.Count > (currentUndo + 1))
            {
                _undos = _undos.GetRange(0, currentUndo + 1);
            }
            _undos.Add(state);
            currentUndo = _undos.Count - 1;
        }
        public UndoState? GetPrevState()
        {
            if (currentUndo > 0 && _undos.Count > 0)
            {
                currentUndo--;
                return _undos[currentUndo];
            }
            return null;
        }

        public UndoState? GetNextState()
        {
            if (_undos.Count >= currentUndo - 2 && currentUndo < _undos.Count - 1)
            {
                currentUndo++;
                return _undos[currentUndo];
            }
            return null;
        }
    }
}
