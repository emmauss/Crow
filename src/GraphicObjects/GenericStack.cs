﻿using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.ComponentModel;

namespace Crow
{
    public class GenericStack : Group
    {
		internal class Page
		{
			public byte[] bmp;
			public int Size;
		}

		#region CTOR
		public GenericStack()
			: base()
		{            
		}
		#endregion

		#region Private fields
        int _spacing;
        Orientation _orientation;
		#endregion

		public override T AddChild<T> (T child)
		{
			T tmp = base.AddChild (child);
			this.RegisterForLayouting (LayoutingType.PositionChildren);
			return tmp;
		}
		public override void RemoveChild (GraphicObject child)
		{
			base.RemoveChild (child);
			this.RegisterForLayouting (LayoutingType.PositionChildren);
		}

		#region Public Properties
        [XmlAttributeAttribute()][DefaultValue(2)]
        public int Spacing
        {
			get { return _spacing; }
            set { 
				if (_spacing == value)
					return;
				_spacing = value; 
				NotifyValueChanged ("Spacing", Spacing);
			}
        }
        [XmlAttributeAttribute()][DefaultValue(Orientation.Horizontal)]
        public virtual Orientation Orientation
        {
            get { return _orientation; }
            set { _orientation = value; }
        }
		#endregion

		#region GraphicObject Overrides
		protected override Size measureRawSize ()
		{
			Size tmp = new Size ();

			if (Orientation == Orientation.Horizontal) {
				foreach (GraphicObject c in Children) {
					if (!c.Visible)
						continue;					
					tmp.Width += c.Slot.Width + Spacing;
					tmp.Height = Math.Max (tmp.Height, Math.Max(c.Slot.Bottom, c.Slot.Height));
				}
				if (tmp.Width > 0)
					tmp.Width -= Spacing;
			} else {
				foreach (GraphicObject c in Children) {
					if (!c.Visible)
						continue;					
					tmp.Width = Math.Max (tmp.Width, Math.Max(c.Slot.Right, c.Slot.Width));
					tmp.Height += c.Slot.Height + Spacing;
				}
				if (tmp.Height > 0)
					tmp.Height -= Spacing;
			}

			tmp.Width += 2 * Margin;
			tmp.Height += 2 * Margin;

			return tmp;
		}
		public virtual void ComputeChildrenPositions()
		{
			#if DEBUG_LAYOUTING
			Debug.WriteLine("ComputeChildrenPosition: " + this.ToString());
			#endif
			int d = 0;
			if (Orientation == Orientation.Horizontal) {
				foreach (GraphicObject c in Children) {
					if (!c.Visible)
						continue;
					c.Slot.X = d;
					d += c.Slot.Width + Spacing;
					c.RegisterForLayouting (LayoutingType.Y);
				}
			} else {
				foreach (GraphicObject c in Children) {
					if (!c.Visible)
						continue;					
					c.Slot.Y = d;
					d += c.Slot.Height + Spacing;
					c.RegisterForLayouting (LayoutingType.X);
				}
			}
			bmp = null;
		}
			
		public override bool UpdateLayout (LayoutingType layoutType)
        {
			RegisteredLayoutings &= (~layoutType);

			LayoutingType childSizeToCheck;
			if (Orientation == Orientation.Horizontal)
				childSizeToCheck = LayoutingType.Width;
			else
				childSizeToCheck = LayoutingType.Height;

			foreach (GraphicObject g in Children) {
				if (!g.Visible)
					continue;
				if (g.RegisteredLayoutings.HasFlag (childSizeToCheck))
					return false;						
			}

			if (layoutType == LayoutingType.PositionChildren) {
				//allow 1 child to have size to 0 if stack has fixed or streched size,
				//this child will occupy remaining space
				if (Orientation == Orientation.Horizontal) {
					if (Width >= 0) {
						GraphicObject stretchedGO = null;
						int tmpWidth = Slot.Width;
						int cptChildren = 0;
						for (int i = 0; i < Children.Count; i++) {
							if (!Children [i].Visible)
								continue;
							cptChildren++;
							if (Children [i].Width == 0) {
								if (stretchedGO != null)
									throw new Exception ("Only one child in stack may have size to stretched");
								stretchedGO = Children [i];
								if (i < Children.Count - 1)
									tmpWidth -= Spacing;
								continue;
							}
							tmpWidth -= Children[i].Slot.Width + Spacing;
						}
						if (stretchedGO != null) {
							tmpWidth += (Spacing - 2 * Margin);
							if (tmpWidth < MinimumSize.Width)
								tmpWidth = MinimumSize.Width;
							else if (tmpWidth > MaximumSize.Width && MaximumSize.Width > 0)
								tmpWidth = MaximumSize.Width;
							if (stretchedGO.LastSlots.Width != tmpWidth) {
								stretchedGO.Slot.Width = tmpWidth;
								stretchedGO.bmp = null;
								stretchedGO.OnLayoutChanges (LayoutingType.Width);
								stretchedGO.LastSlots.Width = stretchedGO.Slot.Width;
							}
						}
					}					
				} else {
					if (Height >= 0) {
						GraphicObject stretchedGO = null;
						int tmpHeight = Slot.Height;
						int cptChildren = 0;
						for (int i = 0; i < Children.Count; i++) {
							if (!Children [i].Visible)
								continue;
							cptChildren++;
							if (Children [i].Height == 0) {
								if (stretchedGO != null)
									throw new Exception ("Only one child in stack may have size to stretched");
								stretchedGO = Children [i];
								if (i < Children.Count - 1)
									tmpHeight -= Spacing;
								continue;
							}
							tmpHeight -= Children[i].Slot.Height + Spacing;
						}
						if (stretchedGO != null) {
							tmpHeight += (Spacing - 2 * Margin);
							if (tmpHeight < MinimumSize.Height)
								tmpHeight = MinimumSize.Height;
							else if (tmpHeight > MaximumSize.Height && MaximumSize.Height > 0)
								tmpHeight = MaximumSize.Height;
							if (stretchedGO.LastSlots.Height != tmpHeight) {
								stretchedGO.Slot.Height = tmpHeight;
								stretchedGO.bmp = null;
								stretchedGO.OnLayoutChanges (LayoutingType.Height);
								stretchedGO.LastSlots.Height = stretchedGO.Slot.Height;
							}
						}
					}
				}

				ComputeChildrenPositions ();

				//if no layouting remains in queue for item, registre for redraw
				if (RegisteredLayoutings == LayoutingType.None && bmp==null)
					this.RegisterForRedraw ();

				return true;
			}

			return base.UpdateLayout(layoutType);
        }

		public override void OnChildLayoutChanges (object sender, LayoutingEventArgs arg)
		{
			base.OnChildLayoutChanges (sender, arg);

			GraphicObject g = sender as GraphicObject;
			switch (arg.LayoutType) {
			case LayoutingType.Width:
				if (Orientation == Orientation.Horizontal) {
					if (this.Bounds.Width < 0)
						this.RegisterForLayouting (LayoutingType.Width);
					this.RegisterForLayouting (LayoutingType.PositionChildren);
				}
				break;
			case LayoutingType.Height:
				if (Orientation == Orientation.Vertical) {
					if (this.Bounds.Height < 0)
						this.RegisterForLayouting (LayoutingType.Height);
					this.RegisterForLayouting (LayoutingType.PositionChildren);
				}
				break;
			}
		}
		#endregion

    
	}
}
