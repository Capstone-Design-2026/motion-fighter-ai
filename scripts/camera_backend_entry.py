from redbool_demo.backend import main

if __name__=='__main__':
    import multiprocessing
    import sys
    multiprocessing.freeze_support()
    # Unity reads redirected UTF-8 on Windows too, including Korean messages.
    for stream in (sys.stdout,sys.stderr):
        if stream is not None and hasattr(stream,'reconfigure'):
            stream.reconfigure(encoding='utf-8',errors='backslashreplace')
    from redbool_demo.backend_cache import configure_cache
    configure_cache()
    main()
