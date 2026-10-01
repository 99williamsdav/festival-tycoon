class_name CleanupPoseProof
extends RefCounted
# Presentation reference only: caller supplies normalized authoritative phase and root-local contact.
static func vec(a:Array)->Vector3:return Vector3(a[0],a[1],a[2])
static func node(root:Node,n:String)->Node3D:return root.find_child(n,true,false) as Node3D
static func smooth_phase(t:float)->float:return t*t*(3.0-2.0*t)
static func arm(root:Node3D,data:Dictionary,side:String,hip:Vector3,lean:Basis,hand:Transform3D)->Dictionary:
	var j=data.joint_nodes[side]
	var s0=vec(j.shoulder_godot);var e0=vec(j.elbow_godot);var w0=vec(j.hand_pivot_godot)
	var shoulder=hip+lean*(s0-hip);var wrist=hand.origin
	var upper=float(j.upper_length);var lower=float(j.fore_length)
	var delta=wrist-shoulder;var d=delta.length();var axis=delta.normalized()
	var reachable=d<upper+lower-.0001 and d>abs(upper-lower)+.0001
	d=clampf(d,abs(upper-lower)+.0001,upper+lower-.0001)
	var pole=Vector3(-.2 if side=="Left" else .2,-.2,-1)
	pole=(pole-axis*pole.dot(axis)).normalized()
	var along=(upper*upper-lower*lower+d*d)/(2*d)
	var elbow=shoulder+axis*along+pole*sqrt(maxf(0,upper*upper-along*along))
	var u=node(root,side+"UpperPivot");u.position=shoulder;u.quaternion=Quaternion((e0-s0).normalized(),(elbow-shoulder).normalized())
	var f=node(root,side+"ForePivot");f.position=elbow;f.quaternion=Quaternion((w0-e0).normalized(),(wrist-elbow).normalized())
	node(root,side+"HandPivot").transform=hand
	return {"reachable":reachable,"distance":delta.length(),"upper":upper,"lower":lower}
static func apply(root:Node3D,data:Dictionary,phase:float,contact:Vector3,picker:Node3D,bag:Node3D)->Dictionary:
	var female=float(data.height_m)<1.75
	var hip=vec(data.hip_godot)
	var bag_grip=Vector3(-.26 if female else -.28,.89 if female else .93,-.07)
	var bag_mouth=bag_grip+Vector3(-.175,0,0)
	var ready=Vector3(.22 if female else .24,.91 if female else .95,-.10)
	var reach=Vector3(.17 if female else .18,0,-.14-.25*Vector2(contact.x,contact.z).length())
	var h=Vector2(contact.x-reach.x,contact.z-reach.z).length()
	reach.y=contact.y+sqrt(maxf(.01,.8*.8-h*h))
	var transfer=Vector3(.24 if female else .26,0,-.34)
	h=Vector2(bag_mouth.x-transfer.x,bag_mouth.z-transfer.z).length()
	transfer.y=bag_mouth.y+sqrt(maxf(.01,.8*.8-h*h))
	var grip:Vector3
	var direction:Vector3
	var bend:float
	if phase<0:
		grip=ready;direction=Vector3(.05,-1,-.08).normalized();bend=0
	elif phase>1:
		# Optional cosmetic recovery only AFTER authoritative completion. Pause-aware visual time.
		var t=smooth_phase(clampf((phase-1)/.15,0,1))
		grip=transfer.lerp(ready,t)
		direction=(bag_mouth-transfer).normalized().lerp(Vector3(.05,-1,-.08).normalized(),t).normalized()
		bend=0
	elif phase<=.5:
		var t=smooth_phase(clampf(phase/.45,0,1))
		grip=ready.lerp(reach,t)
		direction=Vector3(.05,-1,-.08).normalized().lerp((contact-reach).normalized(),t).normalized()
		bend=deg_to_rad(32)*t
	else:
		var t=smooth_phase(clampf((phase-.5)/.35,0,1))
		grip=reach.lerp(transfer,t)
		direction=(contact-reach).normalized().lerp((bag_mouth-transfer).normalized(),t).normalized()
		bend=deg_to_rad(32)*(1-t)
	var lean=Basis(Vector3.RIGHT,-bend)
	node(root,"UpperPivot").transform=Transform3D(lean,hip)
	var tool_basis=Basis(Quaternion(Vector3.DOWN,direction))
	picker.transform=Transform3D(tool_basis,grip)
	bag.transform=Transform3D(Basis.IDENTITY,bag_grip)
	var right_hand=Transform3D(tool_basis,grip+tool_basis*Vector3(0,.044,0))
	var left_hand=Transform3D(Basis.IDENTITY,bag_grip+Vector3(0,.044,0))
	var right=arm(root,data,"Right",hip,lean,right_hand)
	var left=arm(root,data,"Left",hip,lean,left_hand)
	return {"right":right,"left":left,"picker_grip":grip,"jaw":grip+direction*.8,"bag_mouth":bag_mouth,"bag_bottom":bag_grip.y-.48,"lean_degrees":rad_to_deg(bend)}
