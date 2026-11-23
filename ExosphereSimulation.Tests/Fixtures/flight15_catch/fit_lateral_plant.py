import numpy as np, sys
from scipy import signal, optimize
d=np.loadtxt(sys.argv[1])
for (n,amp) in sorted(set((int(r[0]),r[1]) for r in d)):
    m=d[(d[:,0]==n)&(d[:,1]==amp)]
    t=m[:,2]; u=m[:,3]; y=m[:,4]; tilt=m[:,6]; thr=m[:,7]
    sel=(t>=3.0)&(t<=24)
    t=t[sel]-t[sel][0]; u=u[sel]; y=y[sel]
    def sim(p):
        K,Tz,T,z,b=p
        sys_=signal.lti([-K*Tz,K],[T*T,2*z*T,1])
        _,yo,_=signal.lsim(sys_,u,t)
        return yo+b
    r=optimize.least_squares(lambda p: sim(p)-y,[1.0,0.5,1.0,0.8,0.0],bounds=([0,0,0.05,0.1,-2],[3,5,10,3,2]))
    K,Tz,T,z,b=r.x
    rms=np.sqrt(np.mean(r.fun**2))
    st=(m[:,2]>=4)&(m[:,2]<12)
    dip=m[st,4].min()
    print(f"n={n} amp={amp}: K={K:.3f} Tz={Tz:.3f}s T={T:.3f}s zeta={z:.3f} bias={b:.3f} rms={rms:.3f} | dip={dip:.2f} thr={thr[st].mean():.2f} tiltSS={tilt[(m[:,2]>10)&(m[:,2]<12)].mean():.1f}")
