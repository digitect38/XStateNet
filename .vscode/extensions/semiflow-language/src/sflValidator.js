"use strict";
exports.__esModule = true;
exports.SFLValidator = void 0;
var SFLValidator = /** @class */ (function () {
    function SFLValidator() {
        this.schedulingRules = [
            'PSR_001', 'PSR_002', 'PSR_003',
            'WAR_001', 'WAR_002',
            'SSR_001', 'SSR_002'
        ];
    }
    SFLValidator.prototype.validateDocument = function (text) {
        var _this = this;
        var diagnostics = [];
        var lines = text.split('\n');
        lines.forEach(function (line, index) {
            if (line.includes('APPLY_RULE')) {
                var match = line.match(/"([^"]+)"/);
                if (match && !_this.schedulingRules.includes(match[1])) {
                    diagnostics.push({
                        line: index,
                        message: "Unknown rule: ".concat(match[1])
                    });
                }
            }
        });
        return diagnostics;
    };
    return SFLValidator;
}());
exports.SFLValidator = SFLValidator;
