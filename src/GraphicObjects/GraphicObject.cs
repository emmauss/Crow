﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Cairo;
using System.Linq;
using System.Diagnostics;
using System.IO;

namespace Crow
{
	public class GraphicObject : IXmlSerializable, ILayoutable, IValueChange, ICloneable
	{
		#if DEBUG_LAYOUTING
		internal static ulong currentUid = 0;
		internal ulong uid = 0;
		#endif

		internal List<Binding> Bindings = new List<Binding> ();
		internal int layoutingTries = 0;

		Rectangles clipping = new Rectangles();
		public Rectangles Clipping { get { return clipping; }}

		#region IValueChange implementation
		public event EventHandler<ValueChangeEventArgs> ValueChanged;
		public virtual void NotifyValueChanged(string MemberName, object _value)
		{
			ValueChanged.Raise(this, new ValueChangeEventArgs(MemberName, _value));
		}
		#endregion

		#region CTOR
		public GraphicObject ()
		{
			#if DEBUG_LAYOUTING
			uid = currentUid;
			currentUid++;
			#endif

			if (Interface.XmlSerializerInit)
				return;

			loadDefaultValues ();
		}
		public GraphicObject (Rectangle _bounds)
		{
			if (Interface.XmlSerializerInit)
				return;

			loadDefaultValues ();
			Bounds = _bounds;
		}
		#endregion

		#region private fields
		LayoutingType registeredLayoutings = LayoutingType.None;
		ILayoutable logicalParent;
		ILayoutable _parent;
		string _name = "unamed";
		Fill _background = Color.Transparent;
		Fill _foreground = Color.White;
		Font _font = "droid, 10";
		double _cornerRadius = 0;
		int _margin = 0;
		bool _focusable = false;
		bool _hasFocus = false;
		bool _isActive = false;
		bool _mouseRepeat;
		protected bool _isVisible = true;
		VerticalAlignment _verticalAlignment = VerticalAlignment.Center;
		HorizontalAlignment _horizontalAlignment = HorizontalAlignment.Center;
		Size _maximumSize = "0;0";
		Size _minimumSize = "0;0";
		bool cacheEnabled = false;
		object dataSource;
		string style;
		#endregion

		#region public fields
		/// <summary>
		/// Original size and position 0=Stretched; -1=Fit
		/// </summary>
		public Rectangle Bounds;
		/// <summary>
		/// Current size and position computed during layouting pass
		/// </summary>
		public Rectangle Slot = new Rectangle ();
		/// <summary>
		/// keep last slot components for each layouting pass to track
		/// changes and trigger update of other component accordingly
		/// </summary>
		public Rectangle LastSlots;
		/// <summary>
		/// keep last slot painted on screen to clear traces if moved or resized
		/// TODO: we should ensure the whole parsed widget tree is the last painted
		/// version to clear effective oldslot if parents have been moved or resized.
		/// IDEA is to add a ScreenCoordinates function that use only lastPaintedSlots
		/// </summary>
		public Rectangle LastPaintedSlot;
		public object Tag;
		public byte[] bmp;
		#endregion

		#region ILayoutable
		[XmlIgnore]public LayoutingType RegisteredLayoutings { get { return registeredLayoutings; } set { registeredLayoutings = value; } }
		//TODO: it would save the recurent cost of a cast in event bubbling if parent type was GraphicObject
		//		or we could add to the interface the mouse events
		/// <summary>
		/// Parent in the graphic tree, used for rendering and layouting
		/// </summary>
		[XmlIgnore]public virtual ILayoutable Parent {
			get { return _parent; }
			set { _parent = value; }
		}

		public ILayoutable LogicalParent {
			get { return logicalParent == null ? Parent : logicalParent; }
			set { logicalParent = value; }
		}

		[XmlIgnore]public virtual Rectangle ClientRectangle {
			get {
				Rectangle cb = Slot.Size;
				cb.Inflate ( - Margin);
				return cb;
			}
		}

		public virtual Rectangle ContextCoordinates(Rectangle r){
			GraphicObject go = Parent as GraphicObject;
			if (go == null)
				return r + Parent.ClientRectangle.Position;
			return go.CacheEnabled ?
				r + Parent.ClientRectangle.Position :
				Parent.ContextCoordinates (r);
		}
		public virtual Rectangle ScreenCoordinates (Rectangle r){
			return
				Parent.ScreenCoordinates(r) + Parent.getSlot().Position + Parent.ClientRectangle.Position;
		}
		public virtual Rectangle getSlot() => Slot;
		public virtual Rectangle getBounds() => Bounds;
		#endregion

		#region EVENT HANDLERS
		public event EventHandler<MouseWheelEventArgs> MouseWheelChanged;
		public event EventHandler<MouseButtonEventArgs> MouseUp;
		public event EventHandler<MouseButtonEventArgs> MouseDown;
		public event EventHandler<MouseButtonEventArgs> MouseClick;
		public event EventHandler<MouseMoveEventArgs> MouseMove;
		public event EventHandler<MouseMoveEventArgs> MouseEnter;
		public event EventHandler<MouseMoveEventArgs> MouseLeave;
		public event EventHandler<KeyboardKeyEventArgs> KeyDown;
		public event EventHandler<KeyboardKeyEventArgs> KeyUp;
		public event EventHandler Focused;
		public event EventHandler Unfocused;
		public event EventHandler<LayoutingEventArgs> LayoutChanged;
		#endregion

		#region public properties
		[XmlAttributeAttribute()][DefaultValue(true)]
		public virtual bool CacheEnabled {
			get { return cacheEnabled; }
			set {
				if (cacheEnabled == value)
					return;
				cacheEnabled = value;
				NotifyValueChanged ("CacheEnabled", cacheEnabled);
			}
		}
		[XmlAttributeAttribute()][DefaultValue("unamed")]
		public virtual string Name {
			get { return _name; }
			set {
				if (_name == value)
					return;
				_name = value;
				NotifyValueChanged("Name", _verticalAlignment);
			}
		}
		[XmlAttributeAttribute	()][DefaultValue(VerticalAlignment.Center)]
		public virtual VerticalAlignment VerticalAlignment {
			get { return _verticalAlignment; }
			set {
				if (_verticalAlignment == value)
					return;

				_verticalAlignment = value;
				NotifyValueChanged("VerticalAlignment", _verticalAlignment);
			}
		}
		[XmlAttributeAttribute()][DefaultValue(HorizontalAlignment.Center)]
		public virtual HorizontalAlignment HorizontalAlignment {
			get { return _horizontalAlignment; }
			set {
				if (_horizontalAlignment == value)
					return;

				_horizontalAlignment = value;
				NotifyValueChanged("HorizontalAlignment", _horizontalAlignment);
			}
		}
		[XmlAttributeAttribute()][DefaultValue(0)]
		public virtual int Left {
			get { return Bounds.X; }
			set {
				if (Bounds.X == value)
					return;

				Bounds.X = value;
				NotifyValueChanged ("Left", Bounds.X);
				this.RegisterForLayouting (LayoutingType.X);
			}
		}
		[XmlAttributeAttribute()][DefaultValue(0)]
		public virtual int Top {
			get { return Bounds.Y; }
			set {
				if (Bounds.Y == value)
					return;

				Bounds.Y = value;
				NotifyValueChanged ("Top", Bounds.Y);
				this.RegisterForLayouting (LayoutingType.Y);
			}
		}
		[XmlAttributeAttribute()][DefaultValue(0)]
		public virtual int Width {
			get { return Bounds.Width; }
			set {
				if (Bounds.Width == value)
					return;

				Bounds.Width = value;
				NotifyValueChanged ("Width", Bounds.Width);
				NotifyValueChanged ("WidthPolicy", WidthPolicy);

				this.RegisterForLayouting (LayoutingType.Width);
			}
		}
		[XmlAttributeAttribute()][DefaultValue(0)]
		public virtual int Height {
			get { return Bounds.Height; }
			set {
				if (Bounds.Height == value)
					return;

				Bounds.Height = value;
				NotifyValueChanged ("Height", Bounds.Height);
				NotifyValueChanged ("HeightPolicy", HeightPolicy);

				this.RegisterForLayouting (LayoutingType.Height);
			}
		}
		[XmlIgnore]public virtual int WidthPolicy { get { return Width < 1 ? Width : 0; } }
		[XmlIgnore]public virtual int HeightPolicy { get { return Height < 1 ? Height : 0; } }

		[XmlAttributeAttribute()][DefaultValue(false)]
		public virtual bool Fit {
			get { return Width < 0 && Height < 0 ? true : false; }
			set {
				if (value == Fit)
					return;

				Width = Height = -1;
			}
		}
		[XmlAttributeAttribute()][DefaultValue(false)]
		public virtual bool Focusable {
			get { return _focusable; }
			set {
				if (_focusable == value)
					return;
				_focusable = value;
				NotifyValueChanged ("Focusable", _focusable);
			}
		}
		[XmlIgnore]public virtual bool HasFocus {
			get { return _hasFocus; }
			set {
				if (value == _hasFocus)
					return;

				_hasFocus = value;
				NotifyValueChanged ("HasFocus", _hasFocus);
			}
		}
		[XmlIgnore]public virtual bool IsActive {
			get { return _isActive; }
			set {
				if (value == _isActive)
					return;

				_isActive = value;
				NotifyValueChanged ("IsActive", _isActive);
			}
		}
		[XmlAttributeAttribute()][DefaultValue(false)]
		public virtual bool MouseRepeat {
			get { return _mouseRepeat; }
			set {
				if (_mouseRepeat == value)
					return;
				_mouseRepeat = value;
				NotifyValueChanged ("MouseRepeat", _mouseRepeat);
			}
		}
		[XmlAttributeAttribute()][DefaultValue("Transparent")]
		public virtual Fill Background {
			get { return _background; }
			set {
				if (_background == value)
					return;
				_background = value;
				NotifyValueChanged ("Background", _background);
				RegisterForGraphicUpdate ();
			}
		}
		[XmlAttributeAttribute()][DefaultValue("White")]
		public virtual Fill Foreground {
			get { return _foreground; }
			set {
				if (_foreground == value)
					return;
				_foreground = value;
				NotifyValueChanged ("Foreground", _foreground);
				RegisterForGraphicUpdate ();
			}
		}
		[XmlAttributeAttribute()][DefaultValue("droid,10")]
		public virtual Font Font {
			get { return _font; }
			set {
				if (value == _font)
					return;
				_font = value;
				NotifyValueChanged ("Font", _font);
				RegisterForGraphicUpdate ();
			}
		}
		[XmlAttributeAttribute()][DefaultValue(0.0)]
		public virtual double CornerRadius {
			get { return _cornerRadius; }
			set {
				if (value == _cornerRadius)
					return;
				_cornerRadius = value;
				NotifyValueChanged ("CornerRadius", _cornerRadius);
				RegisterForGraphicUpdate ();
			}
		}
		[XmlAttributeAttribute()][DefaultValue(0)]
		public virtual int Margin {
			get { return _margin; }
			set {
				if (value == _margin)
					return;
				_margin = value;
				NotifyValueChanged ("Margin", _margin);
				RegisterForGraphicUpdate ();
			}
		}
		[XmlAttributeAttribute()][DefaultValue(true)]
		public virtual bool Visible {
			get { return _isVisible; }
			set {
				if (value == _isVisible)
					return;

				_isVisible = value;

				if (Interface.CurrentInterface == null)
					return;

				//ensure main win doesn't keep hidden childrens ref
				if (!_isVisible && this.Contains (Interface.CurrentInterface.hoverWidget))
					Interface.CurrentInterface.hoverWidget = null;

				if (Parent is GraphicObject)
					(Parent as GraphicObject).RegisterForLayouting (LayoutingType.Sizing);
				if (Parent is GenericStack)
					(Parent as GraphicObject).RegisterForLayouting (LayoutingType.ArrangeChildren);

				RegisterForLayouting (LayoutingType.Sizing);
				RegisterForGraphicUpdate ();

				NotifyValueChanged ("Visible", _isVisible);
			}
		}
		[XmlAttributeAttribute()][DefaultValue("1;1")]
		public virtual Size MinimumSize {
			get { return _minimumSize; }
			set {
				if (value == _minimumSize)
					return;

				_minimumSize = value;

				NotifyValueChanged ("MinimumSize", _minimumSize);
				RegisterForLayouting (LayoutingType.Sizing);
			}
		}
		[XmlAttributeAttribute()][DefaultValue("0;0")]
		public virtual Size MaximumSize {
			get { return _maximumSize; }
			set {
				if (value == _maximumSize)
					return;

				_maximumSize = value;

				NotifyValueChanged ("MaximumSize", _maximumSize);
				RegisterForLayouting (LayoutingType.Sizing);
			}
		}
		[XmlAttributeAttribute][DefaultValue(null)]
		public virtual object DataSource {
			set {
				if (dataSource == value)
					return;
				#if DEBUG_BINDING
				Debug.WriteLine("******************************");
				Debug.WriteLine("New DataSource for => " + this.ToString());
				Debug.WriteLine("\t- " + DataSource);
				Debug.WriteLine("\t+ " + value);
				#endif

				this.ClearBinding ();

				dataSource = value;

				this.ResolveBindings();

				NotifyValueChanged ("DataSource", dataSource);
			}
			get {
				return dataSource == null ? LogicalParent == null ? null :
					LogicalParent is GraphicObject ?
					(LogicalParent as GraphicObject).DataSource : null : dataSource;
			}
		}
		[XmlAttributeAttribute]
		public virtual string Style {
			get { return style; }
			set {
				if (value == style)
					return;

				style = value;

				NotifyValueChanged ("Style", style);
			}
		}
		#endregion

		/// <summary> Loads the default values from XML attributes default </summary>
		protected virtual void loadDefaultValues()
		{
			#if DEBUG_LOAD
			Debug.WriteLine ("LoadDefValues for " + this.ToString ());
			#endif

			Type thisType = this.GetType ();
			if (Interface.DefaultValuesLoader.ContainsKey(thisType.FullName)) {
				Interface.DefaultValuesLoader[thisType.FullName] (this);
				applyStyle ();
				return;
			}

			//Reflexion being very slow compared to dyn method or delegates,
			//I compile the initial values coded in the CustomAttribs of the class,
			//all other instance of this type would not longer use reflexion to init properly
			//but will fetch the  dynamic initialisation method compiled for this precise type
			//TODO:measure speed gain.
			#region Delfault values Loading dynamic compilation
			DynamicMethod dm = null;
			ILGenerator il = null;

			dm = new DynamicMethod("dyn_loadDefValues",
				MethodAttributes.Family | MethodAttributes.FamANDAssem | MethodAttributes.NewSlot,
				CallingConventions.Standard,
				typeof(void),new Type[] {typeof(object)},thisType,true);

			il = dm.GetILGenerator(256);

			il.Emit(OpCodes.Nop);

			foreach (PropertyInfo pi in thisType.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
				string name = "";
				object defaultValue = null;

				#region retrieve custom attributes
				if (pi.GetSetMethod () == null)
					continue;

				XmlIgnoreAttribute xia = (XmlIgnoreAttribute)pi.GetCustomAttribute (typeof(XmlIgnoreAttribute));
				if (xia != null)
					continue;
				XmlAttributeAttribute xaa = (XmlAttributeAttribute)pi.GetCustomAttribute (typeof(XmlAttributeAttribute));
				if (xaa != null) {
					if (string.IsNullOrEmpty (xaa.AttributeName))
						name = pi.Name;
					else
						name = xaa.AttributeName;
				}
				if (name == "Style"){
					//retrieve default value from class attribute
					DefaultStyle defStyle = thisType.GetCustomAttribute<DefaultStyle>();
					if (defStyle != null)
						defaultValue = defStyle.Path;
				}else{
					DefaultValueAttribute dv = (DefaultValueAttribute)pi.GetCustomAttribute (typeof(DefaultValueAttribute));
					if (dv == null)
						continue;
					defaultValue = dv.Value;
				}
				#endregion

				il.Emit (OpCodes.Ldarg_0);

				if (defaultValue == null) {
					il.Emit (OpCodes.Ldnull);
					il.Emit (OpCodes.Callvirt, pi.GetSetMethod ());
					continue;
				}
				Type dvType = defaultValue.GetType ();

				if (dvType.IsValueType) {
					if (pi.PropertyType.IsValueType) {
						if (pi.PropertyType.IsEnum) {
							if (pi.PropertyType != dvType)
								throw new Exception ("Enum mismatch in default values: " + pi.PropertyType.FullName);
							il.Emit (OpCodes.Ldc_I4, Convert.ToInt32 (defaultValue));
						} else {
							switch (Type.GetTypeCode (dvType)) {
							case TypeCode.Boolean:
								if ((bool)defaultValue == true)
									il.Emit (OpCodes.Ldc_I4_1);
								else
									il.Emit (OpCodes.Ldc_I4_0);
								break;
//						case TypeCode.Empty:
//							break;
//						case TypeCode.Object:
//							break;
//						case TypeCode.DBNull:
//							break;
//						case TypeCode.SByte:
//							break;
//						case TypeCode.Decimal:
//							break;
//						case TypeCode.DateTime:
//							break;
							case TypeCode.Char:
								il.Emit (OpCodes.Ldc_I4, Convert.ToChar (defaultValue));
								break;
							case TypeCode.Byte:
							case TypeCode.Int16:
							case TypeCode.Int32:
								il.Emit (OpCodes.Ldc_I4, Convert.ToInt32 (defaultValue));
								break;
							case TypeCode.UInt16:
							case TypeCode.UInt32:
								il.Emit (OpCodes.Ldc_I4, Convert.ToUInt32 (defaultValue));
								break;
							case TypeCode.Int64:
								il.Emit (OpCodes.Ldc_I8, Convert.ToInt64 (defaultValue));
								break;
							case TypeCode.UInt64:
								il.Emit (OpCodes.Ldc_I8, Convert.ToUInt64 (defaultValue));
								break;
							case TypeCode.Single:
								il.Emit (OpCodes.Ldc_R4, Convert.ToSingle (defaultValue));
								break;
							case TypeCode.Double:
								il.Emit (OpCodes.Ldc_R8, Convert.ToDouble (defaultValue));
								break;
							case TypeCode.String:
								il.Emit (OpCodes.Ldstr, Convert.ToString (defaultValue));
								break;
							default:
								il.Emit (OpCodes.Pop);
								continue;
							}
						}
					} else
						throw new Exception ("Expecting valuetype in default values for: " + pi.Name);
				}else{
					//surely a class or struct
					if (dvType != typeof(string))
						throw new Exception ("Expecting String in default values for: " + pi.Name);
					if (pi.PropertyType == typeof(string))
						il.Emit (OpCodes.Ldstr, Convert.ToString (defaultValue));
					else {
						MethodInfo miParse = pi.PropertyType.GetMethod ("Parse", BindingFlags.Static | BindingFlags.Public);
						if (miParse == null)
							throw new Exception ("no Parse method found for: " + pi.PropertyType.FullName);

						il.Emit (OpCodes.Ldstr, Convert.ToString (defaultValue));
						il.Emit (OpCodes.Callvirt, miParse);

						if (miParse.ReturnType != pi.PropertyType)
							il.Emit (OpCodes.Unbox_Any, pi.PropertyType);
					}
				}
				il.Emit (OpCodes.Callvirt, pi.GetSetMethod ());
			}
			il.Emit(OpCodes.Ret);
			#endregion

			Interface.DefaultValuesLoader[thisType.FullName] = (Interface.loadDefaultInvoker)dm.CreateDelegate(typeof(Interface.loadDefaultInvoker));
			Interface.DefaultValuesLoader[thisType.FullName] (this);
			applyStyle ();
		}

		public virtual GraphicObject FindByName(string nameToFind){
			return string.Equals(nameToFind, _name, StringComparison.Ordinal) ? this : null;
		}
		public virtual bool Contains(GraphicObject goToFind){
			return false;
		}
		public virtual void RegisterClip(Rectangle clip){
			if (CacheEnabled && bmp != null)
				Clipping.AddRectangle (clip + ClientRectangle.Position);
			if (Parent != null)
				Parent.RegisterClip (clip + Slot.Position + ClientRectangle.Position);
		}
		public bool IsQueueForGraphicUpdate = false;
		/// <summary>
		/// Clear chached object and add clipping region in redraw list of interface
		/// </summary>
		public void RegisterForGraphicUpdate ()
		{
			Interface.RegisterForGraphicUpdate (this);
		}
		internal bool IsInRedrawList = false;
		/// <summary>
		/// Add clipping region in redraw list of interface, dont update cached object content
		/// </summary>
		internal void AddToRedrawList ()
		{
			Interface.AddToRedrawList (this);
		}

		#region Layouting
		[XmlIgnore]public int LayoutingTries {
			get { return layoutingTries; }
			set { layoutingTries = value; }
		}
		/// <summary> return size of content + margins </summary>
		protected virtual int measureRawSize (LayoutingType lt) {
			return lt == LayoutingType.Width ? Bounds.Size.Width : Bounds.Size.Height;
		}
		/// <summary> By default in groups, LayoutingType.ArrangeChildren is reset </summary>
		public virtual void ChildrenLayoutingConstraints(ref LayoutingType layoutType){

		}
		public virtual bool ArrangeChildren { get { return false; } }
		public virtual void RegisterForLayouting(LayoutingType layoutType){
			if (Parent == null)
				return;
			lock (Interface.CurrentInterface.LayoutMutex) {
				//dont set position for stretched item
				if (Width == 0)
					layoutType &= (~LayoutingType.X);
				if (Height == 0)
					layoutType &= (~LayoutingType.Y);

				if (!ArrangeChildren)
					layoutType &= (~LayoutingType.ArrangeChildren);

				//apply constraints depending on parent type
				if (Parent is GraphicObject)
					(Parent as GraphicObject).ChildrenLayoutingConstraints (ref layoutType);

				//prevent queueing same LayoutingType for this
				layoutType &= (~RegisteredLayoutings);

				if (layoutType == LayoutingType.None)
					return;

				#if DEBUG_LAYOUTING
				Debug.WriteLine ("REGLayout => {1}->{0}", layoutType, this.ToString());
				#endif
				//enqueue LQI LayoutingTypes separately
				if (layoutType.HasFlag (LayoutingType.Width))
					Interface.CurrentInterface.LayoutingQueue.Enqueue (new LayoutingQueueItem (LayoutingType.Width, this));
				if (layoutType.HasFlag (LayoutingType.Height))
					Interface.CurrentInterface.LayoutingQueue.Enqueue (new LayoutingQueueItem (LayoutingType.Height, this));
				if (layoutType.HasFlag (LayoutingType.X))
					Interface.CurrentInterface.LayoutingQueue.Enqueue (new LayoutingQueueItem (LayoutingType.X, this));
				if (layoutType.HasFlag (LayoutingType.Y))
					Interface.CurrentInterface.LayoutingQueue.Enqueue (new LayoutingQueueItem (LayoutingType.Y, this));
				if (layoutType.HasFlag (LayoutingType.ArrangeChildren))
					Interface.CurrentInterface.LayoutingQueue.Enqueue (new LayoutingQueueItem (LayoutingType.ArrangeChildren, this));
			}
		}

		/// <summary> trigger dependant sizing component update </summary>
		public virtual void OnLayoutChanges(LayoutingType  layoutType)
		{
			#if DEBUG_LAYOUTING
			Debug.WriteLine ("\t    " + LastSlots.ToString() + "\n\t => " + Slot.ToString ());
			#endif

			switch (layoutType) {
			case LayoutingType.Width:
				RegisterForLayouting (LayoutingType.X);
				break;
			case LayoutingType.Height:
				RegisterForLayouting (LayoutingType.Y);
				break;
			}
			LayoutChanged.Raise (this, new LayoutingEventArgs (layoutType));
		}

		/// <summary> Update layout component, this is where the computation of alignement
		/// and size take place </summary>
		/// <returns><c>true</c>, if layouting was possible, <c>false</c> if conditions were not
		/// met and LQI has to be re-queued</returns>
		public virtual bool UpdateLayout (LayoutingType layoutType)
		{
			//unset bit, it would be reset if LQI is re-queued
			registeredLayoutings &= (~layoutType);

			switch (layoutType) {
			case LayoutingType.X:
				if (Bounds.X == 0) {

					if (Parent.RegisteredLayoutings.HasFlag (LayoutingType.Width) ||
						RegisteredLayoutings.HasFlag (LayoutingType.Width))
						return false;

					switch (HorizontalAlignment) {
					case HorizontalAlignment.Left:
						Slot.X = 0;
						break;
					case HorizontalAlignment.Right:
						Slot.X = Parent.ClientRectangle.Width - Slot.Width;
						break;
					case HorizontalAlignment.Center:
						Slot.X = Parent.ClientRectangle.Width / 2 - Slot.Width / 2;
						break;
					}
				} else
					Slot.X = Bounds.X;

				if (LastSlots.X == Slot.X)
					break;

				bmp = null;

				OnLayoutChanges (layoutType);

				LastSlots.X = Slot.X;
				break;
			case LayoutingType.Y:
				if (Bounds.Y == 0) {

					if (Parent.RegisteredLayoutings.HasFlag (LayoutingType.Height) ||
						RegisteredLayoutings.HasFlag (LayoutingType.Height))
						return false;

					switch (VerticalAlignment) {
					case VerticalAlignment.Top://this could be processed even if parent Height is not known
						Slot.Y = 0;
						break;
					case VerticalAlignment.Bottom:
						Slot.Y = Parent.ClientRectangle.Height - Slot.Height;
						break;
					case VerticalAlignment.Center:
						Slot.Y = Parent.ClientRectangle.Height / 2 - Slot.Height / 2;
						break;
					}
				}else
					Slot.Y = Bounds.Y;

				if (LastSlots.Y == Slot.Y)
					break;

				bmp = null;

				OnLayoutChanges (layoutType);

				LastSlots.Y = Slot.Y;
				break;
			case LayoutingType.Width:
				if (Width > 0)
					Slot.Width = Width;
				else if (Width < 0) {
					Slot.Width = measureRawSize (LayoutingType.Width);
				}else if (Parent.RegisteredLayoutings.HasFlag (LayoutingType.Width))
					return false;
				else
					Slot.Width = Parent.ClientRectangle.Width;

				//size constrain
				if (Slot.Width < MinimumSize.Width)
					Slot.Width = MinimumSize.Width;
				else if (Slot.Width > MaximumSize.Width && MaximumSize.Width > 0)
					Slot.Width = MaximumSize.Width;

				if (LastSlots.Width == Slot.Width)
					break;

				bmp = null;

				OnLayoutChanges (layoutType);

				LastSlots.Width = Slot.Width;
				break;
			case LayoutingType.Height:
				if (Height > 0)
					Slot.Height = Height;
				else if (Height < 0){
					Slot.Height = measureRawSize (LayoutingType.Height);
				}else if (Parent.RegisteredLayoutings.HasFlag (LayoutingType.Height))
					return false;
				else
					Slot.Height = Parent.ClientRectangle.Height;

				//size constrain
				if (Slot.Height < MinimumSize.Height)
					Slot.Height = MinimumSize.Height;
				else if (Slot.Height > MaximumSize.Height && MaximumSize.Height > 0)
					Slot.Height = MaximumSize.Height;

				if (LastSlots.Height == Slot.Height)
					break;

				bmp = null;

				OnLayoutChanges (layoutType);

				LastSlots.Height = Slot.Height;
				break;
			}

			//if no layouting remains in queue for item, registre for redraw
			if (this.registeredLayoutings == LayoutingType.None && bmp == null)
				this.AddToRedrawList ();

			return true;
		}
		#endregion

		#region Rendering
		/// <summary> This is the common overridable drawing routine to create new widget </summary>
		protected virtual void onDraw(Context gr)
		{
			Rectangle rBack = new Rectangle (Slot.Size);

			Background.SetAsSource (gr, rBack);
			CairoHelpers.CairoRectangle(gr,rBack,_cornerRadius);
			gr.Fill ();
		}

		/// <summary>
		/// Internal drawing context creation on a cached surface limited to slot size
		/// this trigger the effective drawing routine </summary>
		protected virtual void RecreateCache ()
		{
			int stride = 4 * Slot.Width;

			int bmpSize = Math.Abs (stride) * Slot.Height;
			bmp = new byte[bmpSize];

			using (ImageSurface draw =
                new ImageSurface(bmp, Format.Argb32, Slot.Width, Slot.Height, stride)) {
				using (Context gr = new Context (draw)) {
					gr.Antialias = Antialias.Subpixel;
					onDraw (gr);
				}
				draw.Flush ();
			}
		}
		protected virtual void UpdateCache(Context ctx){
			Rectangle rb = Slot + Parent.ClientRectangle.Position;
			using (ImageSurface cache = new ImageSurface (bmp, Format.Argb32, Slot.Width, Slot.Height, 4 * Slot.Width)) {
				//TODO:improve equality test for basic color and Fill
				if (this.Background is SolidColor) {
					if ((this.Background as SolidColor).Equals (Color.Clear)) {
						ctx.Save ();
						ctx.Operator = Operator.Clear;
						ctx.Rectangle (rb);
						ctx.Fill ();
						ctx.Restore ();
					}
				}
				ctx.SetSourceSurface (cache, rb.X, rb.Y);
				ctx.Paint ();
			}
			//Clipping.clearAndClip (ctx);
			Clipping.Reset();
		}
		/// <summary> Chained painting routine on the parent context of the actual cached version
		/// of the widget </summary>
		public virtual void Paint (ref Context ctx)
		{
			if (!Visible)
				return;

			LastPaintedSlot = Slot;

			if (cacheEnabled) {
				if (Slot.Width > Interface.MaxCacheSize || Slot.Height > Interface.MaxCacheSize)
					cacheEnabled = false;
			}

			if (cacheEnabled) {
				if (bmp == null)
					RecreateCache ();

				UpdateCache (ctx);
			} else {
				Rectangle rb = Slot + Parent.ClientRectangle.Position;
				ctx.Save ();

				ctx.Translate (rb.X, rb.Y);

				onDraw (ctx);

				ctx.Restore ();
			}
		}
		#endregion

        #region Keyboard handling
		public virtual void onKeyDown(object sender, KeyboardKeyEventArgs e){
			KeyDown.Raise (sender, e);
		}
        #endregion

		#region Mouse handling
		public virtual bool MouseIsIn(Point m)
		{
			if (!Visible)
				return false;
			if (ScreenCoordinates (Slot).ContainsOrIsEqual (m)) {
				Scroller scr = Parent as Scroller;
				if (scr == null) {
					if (Parent is GraphicObject)
						return (Parent as GraphicObject).MouseIsIn (m);
					else return true;
				}
				return scr.MouseIsIn (scr.savedMousePos);
			}
			return false;
		}
		public virtual void checkHoverWidget(MouseMoveEventArgs e)
		{
			if (Interface.CurrentInterface.hoverWidget != this) {
				Interface.CurrentInterface.hoverWidget = this;
				onMouseEnter (this, e);
			}

			this.onMouseMove (this, e);
		}
		public virtual void onMouseMove(object sender, MouseMoveEventArgs e)
		{
			//bubble event to the top
			GraphicObject p = Parent as GraphicObject;
			if (p != null)
				p.onMouseMove(sender,e);

			MouseMove.Raise (sender, e);
		}
		public virtual void onMouseDown(object sender, MouseButtonEventArgs e){
			if (Interface.CurrentInterface.activeWidget == null)
				Interface.CurrentInterface.activeWidget = this;
			if (this.Focusable && !Interface.FocusOnHover) {
				BubblingMouseButtonEventArg be = e as BubblingMouseButtonEventArg;
				if (be.Focused == null) {
					be.Focused = this;
					Interface.CurrentInterface.FocusedWidget = this;
				}
			}
			//bubble event to the top
			GraphicObject p = Parent as GraphicObject;
			if (p != null)
				p.onMouseDown(sender,e);

			MouseDown.Raise (this, e);
		}
		public virtual void onMouseUp(object sender, MouseButtonEventArgs e){
			//bubble event to the top
			GraphicObject p = Parent as GraphicObject;
			if (p != null)
				p.onMouseUp(sender,e);

			MouseUp.Raise (this, e);

			if (MouseIsIn (e.Position)&&HasFocus)
				onMouseClick(sender,e);
		}
		public virtual void onMouseClick(object sender, MouseButtonEventArgs e){
			MouseClick.Raise (this, e);
		}
		public virtual void onMouseWheel(object sender, MouseWheelEventArgs e){
			GraphicObject p = Parent as GraphicObject;
			if (p != null)
				p.onMouseWheel(this,e);

			MouseWheelChanged.Raise (this, e);
		}
		public virtual void onFocused(object sender, EventArgs e){
			Focused.Raise (this, e);
			this.HasFocus = true;
		}
		public virtual void onUnfocused(object sender, EventArgs e){
			Unfocused.Raise (this, e);
			this.HasFocus = false;
		}
		public virtual void onMouseEnter(object sender, MouseMoveEventArgs e)
		{
			MouseEnter.Raise (this, e);
		}
		public virtual void onMouseLeave(object sender, MouseMoveEventArgs e)
		{
			MouseLeave.Raise (this, e);
		}
		#endregion

		public override string ToString ()
		{
			string tmp ="";

			if (Parent != null)
				tmp = Parent.ToString () + tmp;
			#if DEBUG_LAYOUTING
			return Name == "unamed" ? tmp + "." + this.GetType ().Name + uid.ToString(): tmp + "." + Name;
			#else
			return Name == "unamed" ? tmp + "." + this.GetType ().Name : tmp + "." + Name;
			#endif
		}

		#region Binding
		public virtual void ResolveBindings()
		{
			if (Bindings.Count == 0)
				return;
			#if DEBUG_BINDING
			Debug.WriteLine ("Resolve Bindings => " + this.ToString ());
			#endif
			//grouped bindings by Instance of Source
			Dictionary<object,List<Binding>> resolved = new Dictionary<object, List<Binding>>();

			foreach (Binding b in Bindings) {
				if (b.Resolved)
					continue;

				if (b.Target.Member.MemberType == MemberTypes.Event) {
					if (b.Expression.StartsWith("{")){
						CompileEventSource(b);
						continue;
					}
					if (!b.FindTarget ())
						continue;
					//register handler for event
					if (b.Source.Method == null) {
						Debug.WriteLine ("\tError: Handler Method not found: " + b.ToString());
						continue;
					}
					try {
						MethodInfo addHandler = b.Target.Event.GetAddMethod ();
						Delegate del = Delegate.CreateDelegate (b.Target.Event.EventHandlerType, b.Source.Instance, b.Source.Method);
						addHandler.Invoke (this, new object[] { del });

						#if DEBUG_BINDING
						Debug.WriteLine ("\tHandler binded => " + b.ToString());
						#endif
						b.Resolved = true;
					} catch (Exception ex) {
						Debug.WriteLine ("\tERROR: " + ex.ToString());
					}
					continue;
				}

				if (!b.FindTarget ())
					continue;

				List<Binding> bindings = null;
				if (!resolved.TryGetValue (b.Source.Instance, out bindings)) {
					bindings = new List<Binding> ();
					resolved [b.Source.Instance] = bindings;
				}
				bindings.Add (b);
				b.Resolved = true;

			}

			MethodInfo stringEquals = typeof(string).GetMethod
				("Equals", new Type[3] {typeof(string), typeof(string), typeof(StringComparison)});
			Type sourceType = this.GetType();
			EventInfo ei = typeof(IValueChange).GetEvent("ValueChanged");
			MethodInfo evtInvoke = ei.EventHandlerType.GetMethod ("Invoke");
			ParameterInfo[] evtParams = evtInvoke.GetParameters ();
			Type handlerArgsType = evtParams [1].ParameterType;
			Type[] args = {typeof(object), typeof(object),handlerArgsType};
			FieldInfo fiNewValue = typeof(ValueChangeEventArgs).GetField("NewValue");
			FieldInfo fiMbName = typeof(ValueChangeEventArgs).GetField("MemberName");

			//group;only one dynMethods by target (valuechanged event source)
			//changed value name tested in switch
			//IEnumerable<Binding[]> groupedByTarget = resolved.GroupBy (g => g.Target.Instance, g => g, (k, g) => g.ToArray ());
			foreach (List<Binding> grouped in resolved.Values) {
				int i = 0;
				Type targetType = grouped[0].Source.Instance.GetType();

				DynamicMethod dm = null;
				ILGenerator il = null;

				System.Reflection.Emit.Label[] jumpTable = null;
				System.Reflection.Emit.Label endMethod = new System.Reflection.Emit.Label();

				#region Retrieve EventHandler parameter type
				//EventInfo ei = targetType.GetEvent ("ValueChanged");
				//no dynamic update if ValueChanged interface is not implemented
				if (targetType.GetInterfaces().Contains(typeof(IValueChange))){
					dm = new DynamicMethod(grouped[0].NewDynMethodId,
						MethodAttributes.Family | MethodAttributes.FamANDAssem | MethodAttributes.NewSlot,
						CallingConventions.Standard,
						typeof(void),
						args,
						sourceType,true);

					il = dm.GetILGenerator(256);

					endMethod = il.DefineLabel();
					jumpTable = new System.Reflection.Emit.Label[grouped.Count];
					for (i = 0; i < grouped.Count; i++)
						jumpTable [i] = il.DefineLabel ();
					il.DeclareLocal(typeof(string));
					il.DeclareLocal(typeof(object));

					il.Emit(OpCodes.Nop);
					il.Emit(OpCodes.Ldarg_0);
					//il.Emit(OpCodes.Isinst, sourceType);
					//push new value onto stack
					il.Emit(OpCodes.Ldarg_2);
					il.Emit(OpCodes.Ldfld, fiNewValue);
					il.Emit(OpCodes.Stloc_1);
					//push name
					il.Emit(OpCodes.Ldarg_2);
					il.Emit(OpCodes.Ldfld, fiMbName);
					il.Emit(OpCodes.Stloc_0);
					il.Emit(OpCodes.Ldloc_0);
					il.Emit(OpCodes.Brfalse, endMethod);
				}
				#endregion

				i = 0;
				foreach (Binding b in grouped) {
					#region initialize target with actual value
					object targetValue = null;
					if (b.Source.Member != null){
						if (b.Source.Member.MemberType == MemberTypes.Property)
							targetValue = b.Source.Property.GetGetMethod ().Invoke (b.Source.Instance, null);
						else if (b.Source.Member.MemberType == MemberTypes.Field)
							targetValue = b.Source.Field.GetValue (b.Source.Instance);
						else if (b.Source.Member.MemberType == MemberTypes.Method){
							MethodInfo mthSrc = b.Source.Method;
							if (mthSrc.IsDefined(typeof(ExtensionAttribute), false))
								targetValue = mthSrc.Invoke(null, new object[] {b.Source.Instance});
							else
								targetValue = mthSrc.Invoke(b.Source.Instance, null);
						}else
							throw new Exception ("unandled source member type for binding");
					}else if (string.IsNullOrEmpty(b.Expression))
						targetValue= grouped [0].Source.Instance;//empty binding exp=> bound to target object by default
					//TODO: handle other dest type conversions
					if (b.Target.Property.PropertyType == typeof(string)){
						if (targetValue == null){
							//set default value

						}else
							targetValue = targetValue.ToString ();
					}
					if (targetValue != null)
						b.Target.Property.GetSetMethod ().Invoke
						(this, new object[] { b.Target.Property.PropertyType.Cast(targetValue)});
					else
						b.Target.Property.GetSetMethod ().Invoke
						(this, new object[] { targetValue });
					#endregion

					//if no dyn update, skip jump table
					if (il == null)
						continue;

					il.Emit (OpCodes.Ldloc_0);
					if (b.Source.Member != null)
						il.Emit (OpCodes.Ldstr, b.Source.Member.Name);
					else
						il.Emit (OpCodes.Ldstr, b.Expression.Split('/').LastOrDefault());
					il.Emit (OpCodes.Ldc_I4_4);//StringComparison.Ordinal
					il.Emit (OpCodes.Callvirt, stringEquals);
					il.Emit (OpCodes.Brtrue, jumpTable[i]);
					i++;
				}

				if (il == null)
					continue;

				il.Emit (OpCodes.Br, endMethod);

				i = 0;
				foreach (Binding b in grouped) {

					il.MarkLabel (jumpTable [i]);
					il.Emit(OpCodes.Ldloc_1);

					//by default, target value type is deducted from source member type to allow
					//memberless binding, if targetMember exists, it will be used to determine target
					//value type for conversion
					Type targetValueType = b.Target.Property.PropertyType;
					if (b.Source.Member != null) {
						if (b.Source.Member.MemberType == MemberTypes.Property)
							targetValueType = b.Source.Property.PropertyType;
						else if (b.Source.Member.MemberType == MemberTypes.Field)
							targetValueType = b.Source.Field.FieldType;
						else
							throw new Exception ("unhandle target member type in binding");
					}

					if (b.Target.Property.PropertyType == typeof(string)) {
						MemberReference tostring = new MemberReference (b.Target.Instance);
						if (!tostring.FindMember ("ToString"))
							throw new Exception ("ToString method not found");
						il.Emit (OpCodes.Callvirt, tostring.Method);
					}else if (!targetValueType.IsValueType)
						il.Emit(OpCodes.Castclass, targetValueType);
					else if (b.Target.Property.PropertyType != targetValueType)
						il.Emit(OpCodes.Callvirt, CompilerServices.GetConvertMethod( b.Target.Property.PropertyType ));
					else
						il.Emit(OpCodes.Unbox_Any, b.Target.Property.PropertyType);

					il.Emit(OpCodes.Callvirt, b.Target.Property.GetSetMethod());
					il.Emit (OpCodes.Br, endMethod);
					i++;

				}
				il.MarkLabel(endMethod);
				il.Emit(OpCodes.Pop);
				il.Emit(OpCodes.Ret);

				Delegate del = dm.CreateDelegate(ei.EventHandlerType, this);
				MethodInfo addHandler = ei.GetAddMethod ();
				addHandler.Invoke(grouped [0].Source.Instance, new object[] {del});
			}
		}
		/// <summary>
		/// Compile events expression in GOML attributes
		/// </summary>
		/// <param name="es">Event binding details</param>
		public void CompileEventSource(Binding binding)
		{
			#if DEBUG_BINDING
			Debug.WriteLine ("\tCompile Event Source => " + binding.ToString());
			#endif

			Type sourceType = this.GetType();

			#region Retrieve EventHandler parameter type
			MethodInfo evtInvoke = binding.Target.Event.EventHandlerType.GetMethod ("Invoke");
			ParameterInfo[] evtParams = evtInvoke.GetParameters ();
			Type handlerArgsType = evtParams [1].ParameterType;
			#endregion

			Type[] args = {typeof(object), typeof(object),handlerArgsType};
			DynamicMethod dm = new DynamicMethod(binding.NewDynMethodId,
				typeof(void),
				args,
				sourceType);


			#region IL generation
			ILGenerator il = dm.GetILGenerator(256);

			string src = binding.Expression.Trim();

			if (! (src.StartsWith("{") || src.EndsWith ("}")))
				throw new Exception (string.Format("GOML:Malformed {0} Event handler: {1}", binding.Target.Member.Name, binding.Expression));

			src = src.Substring (1, src.Length - 2);
			string[] srcLines = src.Split (new char[] { ';' });

			foreach (string srcLine in srcLines) {
				string statement = srcLine.Trim ();

				string[] operandes = statement.Split (new char[] { '=' });
				if (operandes.Length < 2) //not an affectation
				{
					continue;
				}
				string lop = operandes [0].Trim ();
				string rop = operandes [operandes.Length-1].Trim ();

				#region LEFT OPERANDES
				GraphicObject lopObj = this;	//default left operand base object is
				//the first arg (object sender) of the event handler

				il.Emit(OpCodes.Ldarg_0);	//load sender ref onto the stack

				string[] lopParts = lop.Split (new char[] { '.' });
				if (lopParts.Length > 1) {//should search also for member of es.Source
					MethodInfo FindByNameMi = typeof(GraphicObject).GetMethod("FindByName");
					for (int j = 0; j < lopParts.Length - 1; j++) {
						il.Emit (OpCodes.Ldstr, lopParts[j]);
						il.Emit(OpCodes.Callvirt, FindByNameMi);
					}
				}

				int i = lopParts.Length -1;

				MemberInfo[] lopMbis = lopObj.GetType().GetMember (lopParts[i]);

				if (lopMbis.Length<1)
					throw new Exception (string.Format("CROW BINDING: Member not found '{0}'", lop));

				OpCode lopSetOC;
				dynamic lopSetMbi;
				Type lopT = null;
				switch (lopMbis[0].MemberType) {
				case MemberTypes.Property:
					PropertyInfo lopPi = sourceType.GetProperty (lopParts[i]);
					MethodInfo dstMi = lopPi.GetSetMethod ();
					lopT = lopPi.PropertyType;
					lopSetMbi = dstMi;
					lopSetOC = OpCodes.Callvirt;
					break;
				case MemberTypes.Field:
					FieldInfo dstFi = sourceType.GetField(lopParts[i]);
					lopT = dstFi.FieldType;
					lopSetMbi = dstFi;
					lopSetOC = OpCodes.Stfld;
					break;
				default:
					throw new Exception (string.Format("GOML:member type not handle: {0}", lopParts[i]));
				}
				#endregion

				#region RIGHT OPERANDES
				if (rop.StartsWith("\'")){
					if (!rop.EndsWith("\'"))
						throw new Exception (string.Format
							("GOML:malformed string constant in handler: {0}", rop));
					string strcst = rop.Substring (1, rop.Length - 2);

					il.Emit(OpCodes.Ldstr,strcst);

				}else{
					if (lopT.IsEnum)
						throw new NotImplementedException();

					MethodInfo lopParseMi = lopT.GetMethod("Parse");
					if (lopParseMi == null)
						throw new Exception (string.Format
							("GOML:no parse method found in: {0}", lopT.Name));
					il.Emit(OpCodes.Ldstr, rop);
					il.Emit(OpCodes.Callvirt, lopParseMi);
					il.Emit(OpCodes.Unbox_Any, lopT);
				}

				#endregion

				//emit left operand assignment
				il.Emit(lopSetOC, lopSetMbi);
			}

			il.Emit(OpCodes.Ret);

			#endregion

			Delegate del = dm.CreateDelegate(binding.Target.Event.EventHandlerType,this);
			MethodInfo addHandler = binding.Target.Event.GetAddMethod ();
			addHandler.Invoke(this, new object[] {del});

			binding.Resolved = true;
		}
		/// <summary>
		/// Remove dynamic delegates by ids from dataSource
		///  and delete ref of this in Shared interface refs
		/// </summary>
		public virtual void ClearBinding(){
			//dont clear binding if dataSource is not null,
			foreach (Binding b in Bindings) {
				try {
					if (!b.Resolved)
						continue;
					//cancel compiled events
					if (b.Source == null){
						continue;
						#if DEBUG_BINDING
						Debug.WriteLine("Clear binding canceled for => " + b.ToString());
						#endif
					}
					if (b.Source.Instance != DataSource){
						#if DEBUG_BINDING
						Debug.WriteLine("Clear binding canceled for => " + b.ToString());
						#endif
						continue;
					}
					#if DEBUG_BINDING
					Debug.WriteLine("ClearBinding => " + b.ToString());
					#endif
					if (string.IsNullOrEmpty (b.DynMethodId)) {
						b.Resolved = false;
						if (b.Target.Member.MemberType == MemberTypes.Event)
							removeEventHandler (b);
						//TODO:check if full reset is necessary
						continue;
					}
					MemberReference mr = null;
					if (b.Source == null)
						mr = b.Target;
					else
						mr = b.Source;
					Type dataSourceType = mr.Instance.GetType();
					EventInfo evtInfo = dataSourceType.GetEvent ("ValueChanged");
					FieldInfo evtFi = CompilerServices.GetEventHandlerField (dataSourceType, "ValueChanged");
					MulticastDelegate multicastDelegate = evtFi.GetValue (mr.Instance) as MulticastDelegate;
					if (multicastDelegate != null) {
						foreach (Delegate d in multicastDelegate.GetInvocationList()) {
							if (d.Method.Name == b.DynMethodId)
								evtInfo.RemoveEventHandler (mr.Instance, d);
						}
					}
					b.Reset ();
				} catch (Exception ex) {
					Debug.WriteLine("\t Error: " + ex.ToString());
				}
			}
		}
		void removeEventHandler(Binding b){
			FieldInfo fiEvt = CompilerServices.GetEventHandlerField (b.Target.Instance.GetType(), b.Target.Member.Name);
			MulticastDelegate multiDel = fiEvt.GetValue (b.Target.Instance) as MulticastDelegate;
			if (multiDel != null) {
				foreach (Delegate d in multiDel.GetInvocationList()) {
					if (d.Method.Name == b.Source.Member.Name)
						b.Target.Event.RemoveEventHandler (b.Target.Instance, d);
				}
			}
		}
		#endregion

		#region IXmlSerializable
		public virtual System.Xml.Schema.XmlSchema GetSchema ()
		{
			return null;
		}
		void affectMember(string name, string value){
			Type thisType = this.GetType ();

			if (string.IsNullOrEmpty (value))
				return;

			MemberInfo mi = thisType.GetMember (name).FirstOrDefault();
			if (mi == null) {
				Debug.WriteLine ("GOML: Unknown attribute in " + thisType.ToString() + " : " + name);
				return;
			}
			if (mi.MemberType == MemberTypes.Event) {
				this.Bindings.Add (new Binding (new MemberReference(this, mi), value));
				return;
			}
			if (mi.MemberType == MemberTypes.Property) {
				PropertyInfo pi = mi as PropertyInfo;

				if (pi.GetSetMethod () == null) {
					Debug.WriteLine ("GOML: Read only property in " + thisType.ToString() + " : " + name);
					return;
				}

				XmlAttributeAttribute xaa = (XmlAttributeAttribute)pi.GetCustomAttribute (typeof(XmlAttributeAttribute));
				if (xaa != null) {
					if (!string.IsNullOrEmpty (xaa.AttributeName))
						name = xaa.AttributeName;
				}
				if (value.StartsWith("{")) {
					//binding
					if (!value.EndsWith("}"))
						throw new Exception (string.Format("GOML:Malformed binding: {0}", value));

					this.Bindings.Add (new Binding (new MemberReference(this, pi), value.Substring (1, value.Length - 2)));
					return;
				}
				if (pi.GetCustomAttribute (typeof(XmlIgnoreAttribute)) != null)
					return;
				if (xaa == null)//not define as xmlAttribute
					return;

				if (pi.PropertyType == typeof(string)) {
					pi.SetValue (this, value, null);
					return;
				}

				if (pi.PropertyType.IsEnum) {
					pi.SetValue (this, Enum.Parse (pi.PropertyType, value), null);
				} else {
					MethodInfo me = pi.PropertyType.GetMethod ("Parse", new Type[] { typeof(string) });
					pi.SetValue (this, me.Invoke (null, new string[] { value }), null);
				}
			}
		}
		void applyStyle(){
			if (string.IsNullOrEmpty (style))
				return;
			Stream s = Interface.GetStreamFromPath (style);
			if (s == null)
				throw new Exception ("Style Path not found: " + style);

			#if DEBUG_LOAD
			Debug.WriteLine ("ApplyStyle for " + this.ToString ());
			#endif
			using (StreamReader sr = new StreamReader (s)) {
				while (!sr.EndOfStream) {
					string tmp = sr.ReadLine ();
					if (string.IsNullOrWhiteSpace (tmp))
						continue;
					int i = tmp.IndexOf ('=');
					if (i < 0)
						continue;
					string name = tmp.Substring (0, i).Trim();
					string value = tmp.Substring (i + 1).Trim ();

					affectMember (name, value);
				}
			}
		}
		public virtual void ReadXml (System.Xml.XmlReader reader)
		{
			if (!reader.HasAttributes)
				return;
			Type thisType = this.GetType ();

			string stylePath = reader.GetAttribute ("Style");

			if (!string.IsNullOrEmpty (style)) {
				Style = stylePath;
				applyStyle ();
			}
			while (reader.MoveToNextAttribute ()) {
				if (reader.Name == "Style")
					continue;

				affectMember (reader.Name, reader.Value);
			}
			reader.MoveToElement();
		}
		public virtual void WriteXml (System.Xml.XmlWriter writer)
		{
			foreach (PropertyInfo pi in this.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
				if (pi.GetSetMethod () == null)
					continue;

				bool isAttribute = false;
				bool hasDefaultValue = false;
				bool ignore = false;
				string name = "";
				object value = null;
				Type valueType = null;


				MemberInfo mi = pi.GetGetMethod ();

				if (mi == null)
					continue;

				value = pi.GetValue (this, null);
				valueType = pi.PropertyType;
				name = pi.Name;



				object[] att = pi.GetCustomAttributes (false);

				foreach (object o in att) {
					XmlAttributeAttribute xaa = o as XmlAttributeAttribute;
					if (xaa != null) {
						isAttribute = true;
						if (string.IsNullOrEmpty (xaa.AttributeName))
							name = pi.Name;
						else
							name = xaa.AttributeName;
						continue;
					}

					XmlIgnoreAttribute xia = o as XmlIgnoreAttribute;
					if (xia != null) {
						ignore = true;
						continue;
					}

					DefaultValueAttribute dv = o as DefaultValueAttribute;
					if (dv != null) {
						if (dv.Value.Equals (value))
							hasDefaultValue = true;
						if (dv.Value.ToString () == value.ToString ())
							hasDefaultValue = true;

						continue;
					}


				}

				if (hasDefaultValue || ignore || value==null)
					continue;

				if (isAttribute)
					writer.WriteAttributeString (name, value.ToString ());
				else {
					if (valueType.GetInterface ("IXmlSerializable") == null)
						continue;

					(pi.GetValue (this, null) as IXmlSerializable).WriteXml (writer);
				}
			}
			foreach (EventInfo ei in this.GetType().GetEvents()) {
				FieldInfo fi = this.GetType().GetField(ei.Name,
					BindingFlags.NonPublic |
					BindingFlags.Instance |
					BindingFlags.GetField);

				Delegate dg = (System.Delegate)fi.GetValue (this);

				if (dg == null)
					continue;

				foreach (Delegate d in dg.GetInvocationList()) {
					if (!d.Method.Name.StartsWith ("<"))//Skipping empty handler, not clear it's trikky
						writer.WriteAttributeString (ei.Name, d.Method.Name);
				}
			}
		}
		#endregion

		#region ICloneable implementation
		public object Clone ()
		{
			Type type = this.GetType ();
			GraphicObject result = (GraphicObject)Activator.CreateInstance (type);

			foreach (PropertyInfo pi in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
				if (pi.GetSetMethod () == null)
					continue;

				if (pi.GetCustomAttribute<XmlIgnoreAttribute> () != null)
					continue;
				if (pi.Name == "DataSource")
					continue;
//				object[] att = pi.GetCustomAttributes (false);
//				foreach (object o in att) {
//					XmlIgnoreAttribute xia = o as XmlIgnoreAttribute;
//					if (xia != null)
//						continue;
//				}

				pi.SetValue(result, pi.GetValue(this));
			}
			return result;
		}
		#endregion
	}
}
