//
// Global init of core components
//

// Init components
var KTComponents = function () {
    // Public methods
    return {
        init: function () {
            KTApp.init();
			KTDrawer.init();
			KTMenu.init();
			KTScroll.init();
			KTSticky.init();
			KTSwapper.init();
			KTToggle.init();
			KTScrolltop.init();
			KTDialer.init();	
			KTImageInput.init();
			KTPasswordMeter.init();	
        }
    }	
}();

// Declare KTApp for Webpack support
if (typeof module !== 'undefined' && typeof module.exports !== 'undefined') {
	window.KTComponents = window.KTComponents = module.exports = KTComponents;
}